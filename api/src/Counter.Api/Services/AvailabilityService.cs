namespace Counter.Api.Services;

using Counter.Api.Contracts;
using Counter.Domain.Availability;
using Counter.Domain.Catalogue;
using Counter.Domain.Pricing;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;

/// <summary>
/// Assembles the answer the part screen needs.
/// </summary>
/// <remarks>
/// Availability and pricing are gathered together and returned in one response.
/// Two calls would mean two chances to show half an answer — a branch grid with
/// no price, or a price with no stock — and a representative on a call cannot use
/// either half on its own.
/// </remarks>
public sealed class AvailabilityService(
    AvailabilityReader availability,
    PricingReader pricing,
    BranchDirectory branches,
    CatalogueProjection catalogue,
    TimeProvider clock)
{
    private readonly AvailabilityReader _availability = availability;
    private readonly PricingReader _pricing = pricing;
    private readonly BranchDirectory _branches = branches;
    private readonly CatalogueProjection _catalogue = catalogue;
    private readonly TimeProvider _clock = clock;

    /// <summary>
    /// Read a part's availability, and what the selected customer pays for it.
    /// </summary>
    /// <param name="partNumber">The part to look up.</param>
    /// <param name="customerAccount">Whose price to quote, or null for list.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    public async Task<AvailabilityResponse> ReadAsync(
        string partNumber,
        string? customerAccount,
        CancellationToken cancellationToken)
    {
        PartAvailability stock = await _availability.ReadAsync(partNumber, cancellationToken);
        PricingView price = await QuoteAsync(stock.Part, customerAccount, cancellationToken);

        IReadOnlyList<BranchAvailability> branchRows =
            await DescribeBranchesAsync(stock.Positions, cancellationToken);

        return new AvailabilityResponse(
            Part: Describe(stock.Part),
            IsStockKnown: stock.IsStockKnown,
            TotalFreeToSell: stock.TotalFreeToSell,
            Branches: branchRows,
            Pricing: price,
            Envelope: ResponseEnvelope.Complete());
    }

    /// <summary>
    /// Search the catalogue, and read live availability for what matched.
    /// </summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="limit">Most results to return.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <remarks>
    /// The catalogue is searched in memory, which is what makes this fast; the
    /// quantities beside each result are read live, which is what makes them
    /// true. Caching what exists is safe, caching how much there is is not.
    /// </remarks>
    public async Task<SearchResponse> SearchAsync(
        string text,
        int limit,
        CancellationToken cancellationToken)
    {
        // Rebuilds if a slow ERP at startup left the catalogue empty, so search
        // recovers on its own rather than needing someone to restart the process.
        await _catalogue.EnsureBuiltAsync(cancellationToken);

        IReadOnlyList<Part> matches = _catalogue.Search(text, limit);
        List<SearchResult> results = new(matches.Count);

        foreach (Part part in matches)
        {
            PartAvailability stock = await _availability.ReadAsync(
                part.PartNumber, cancellationToken);

            results.Add(new SearchResult(
                part.PartNumber,
                part.Description,
                part.Manufacturer,
                part.UnitOfMeasure,
                part.IsDiscontinued,
                stock.TotalFreeToSell));
        }

        return new SearchResponse(results, ResponseEnvelope.Complete());
    }

    /// <summary>Work out what a customer pays, with the terms that did not apply.</summary>
    private async Task<PricingView> QuoteAsync(
        Part part,
        string? customerAccount,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContractTerms> terms = [];

        if (!string.IsNullOrWhiteSpace(customerAccount))
        {
            try
            {
                CustomerAccount customer = await _pricing.ReadCustomerAsync(
                    customerAccount, cancellationToken);

                terms = await _pricing.ReadTermsAsync(
                    customer.PriceClass, part.CategoryCode, cancellationToken);
            }
            catch (ErpRecordNotFoundException)
            {
                // An unknown account is quoted at list rather than refused: the
                // representative can still answer the availability question,
                // which is the more urgent half.
                terms = [];
            }
        }

        QuotedPrice quoted = PriceCalculator.Quote(
            part.ListPrice,
            terms,
            DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));

        return new PricingView(
            ListPrice: quoted.ListPrice,
            NetPrice: quoted.NetPrice,
            Multiplier: quoted.AppliedTerms?.Multiplier,
            Basis: quoted.Basis.ToString(),
            TermsDescription: quoted.AppliedTerms?.Description,
            DisregardedTerms: quoted.DisregardedTerms
                .Select(terms => new DisregardedTermsView(terms.Terms.Description, terms.Reason))
                .ToList());
    }

    /// <summary>Name each branch, keeping rows whose branch is unknown.</summary>
    private async Task<IReadOnlyList<BranchAvailability>> DescribeBranchesAsync(
        IReadOnlyList<BranchPosition> positions,
        CancellationToken cancellationToken)
    {
        List<BranchAvailability> rows = new(positions.Count);

        foreach (BranchPosition position in positions)
        {
            Branch? branch = await _branches.FindAsync(position.BranchCode, cancellationToken);

            rows.Add(new BranchAvailability(
                BranchCode: position.BranchCode,
                // A branch the directory does not know is shown by its code
                // rather than dropped: data nobody has cleaned up should still
                // tell the truth about where stock is.
                BranchName: branch?.Name ?? position.BranchCode,
                City: branch?.City ?? string.Empty,
                OnHand: position.OnHand,
                Committed: position.Committed,
                FreeToSell: position.FreeToSell,
                OnOrder: position.OnOrder,
                Bin: position.Bin,
                StockState: position.State.ToString(),
                IsBranchKnown: branch is not null));
        }

        return rows;
    }

    private static PartSummary Describe(Part part) => new(
        part.PartNumber,
        part.Description,
        part.Manufacturer,
        part.ManufacturerPartNumber,
        part.UnitOfMeasure,
        part.IsDiscontinued);
}
