using Counter.Domain.Availability;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Counter.Infrastructure.Erp;

/// <summary>
/// Parts where some of the stock is already somebody else's.
/// </summary>
/// <remarks>
/// The distinction this whole application is built around — free to sell is on
/// hand minus committed — is invisible on most of the catalogue, because most
/// parts have nothing committed anywhere. Somebody picking a part at random has
/// roughly one chance in twelve of seeing it, reads twelve identical zeroes in a
/// committed column, and reasonably concludes the column is decoration.
///
/// So the empty screen offers a few parts where the difference is real. Not a
/// fixture and not a hardcoded part number: the account is asked, and if the
/// answer is that nothing is committed anywhere — which is what a quiet real ERP
/// would say — nothing is offered and no claim is made.
///
/// Scanned once and kept, because this is read to fill a panel on a screen
/// somebody is waiting for, and reading a few dozen inventory records per page
/// load to populate a hint would be a poor trade.
/// </remarks>
/// <param name="scopes">
/// Used to open a scope for the scan.
///
/// The answer is kept for the lifetime of the process, but the readers that
/// produce it hold a per-request connection to the MCP server and are registered
/// per request accordingly. Taking them as constructor arguments made this a
/// singleton holding scoped services, which the container refuses at startup --
/// correctly, since it would have pinned one request's connection open forever.
/// So the cache is long-lived and the reading is not.
/// </param>
public sealed class PromisedStock(IServiceScopeFactory scopes)
{
    /// <summary>
    /// How many parts to look at before giving up.
    ///
    /// A bound rather than the whole catalogue: three thousand inventory reads
    /// to populate a hint is not a trade worth making, and if a hundred parts
    /// yield no example the answer "none" is near enough true to act on.
    /// </summary>
    private const int PartsExamined = 120;

    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<PromisedPart>? _found;

    /// <summary>
    /// A few parts where at least one branch has stock committed.
    /// </summary>
    /// <param name="wanted">How many to return.</param>
    /// <param name="cancellationToken">Abandons the scan when the caller gives up.</param>
    /// <returns>What was found, which may be nothing.</returns>
    public async Task<IReadOnlyList<PromisedPart>> FindAsync(
        int wanted,
        CancellationToken cancellationToken)
    {
        if (_found is not null)
        {
            return [.. _found.Take(wanted)];
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Checked again inside the gate: several first requests can arrive
            // together, and without this each of them runs the whole scan.
            if (_found is not null)
            {
                return [.. _found.Take(wanted)];
            }

            _found = await ScanAsync(wanted, cancellationToken);
            return _found;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Look through the catalogue until enough examples turn up.
    /// </summary>
    /// <param name="wanted">How many are needed.</param>
    /// <param name="cancellationToken">Abandons the scan when the caller gives up.</param>
    private async Task<IReadOnlyList<PromisedPart>> ScanAsync(
        int wanted,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopes.CreateScope();

        CatalogueProjection catalogue =
            scope.ServiceProvider.GetRequiredService<CatalogueProjection>();
        AvailabilityReader availability =
            scope.ServiceProvider.GetRequiredService<AvailabilityReader>();

        await catalogue.EnsureBuiltAsync(cancellationToken);

        List<PromisedPart> found = [];

        foreach (Domain.Catalogue.Part part in catalogue.Browse(PartsExamined))
        {
            if (found.Count >= wanted)
            {
                break;
            }

            PartAvailability stock;

            try
            {
                stock = await availability.ReadAsync(part.PartNumber, cancellationToken);
            }
            catch (ErpRecordNotFoundException)
            {
                // A part with no inventory record is not an example of anything.
                continue;
            }

            BranchPosition? busiest = stock.Positions
                .Where(position => position.Committed > 0)
                .OrderByDescending(position => position.Committed)
                .FirstOrDefault();

            if (busiest is null)
            {
                continue;
            }

            found.Add(new PromisedPart(
                part.PartNumber,
                part.Description,
                busiest.BranchCode,
                busiest.OnHand,
                busiest.Committed,
                busiest.FreeToSell));
        }

        return found;
    }
}

/// <summary>
/// One part, and the branch that shows the difference most plainly.
/// </summary>
/// <param name="PartNumber">The part.</param>
/// <param name="Description">What it is.</param>
/// <param name="BranchCode">The branch holding committed stock.</param>
/// <param name="OnHand">What is physically there.</param>
/// <param name="Committed">How much of it is already promised.</param>
/// <param name="FreeToSell">What is left to sell, which is the number that answers a customer.</param>
public sealed record PromisedPart(
    string PartNumber,
    string Description,
    string BranchCode,
    int OnHand,
    int Committed,
    int FreeToSell);
