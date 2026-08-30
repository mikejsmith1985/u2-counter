using Counter.Domain.Catalogue;
using Counter.Domain.Pricing;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;

namespace Counter.Infrastructure.Ai;

/// <summary>
/// What a part costs, for one customer or for all of them.
/// </summary>
/// <remarks>
/// Added because the assistant was asked which customer had the best price on a
/// part whose price was on the screen at the time, and answered — accurately —
/// that it had no tool for pricing. The screen reaches the price through a
/// different path entirely, so the model was not being cautious; it genuinely
/// could not see it. A reader watching that happen concludes the assistant is
/// decorative, and they would be half right.
///
/// The comparison is cheaper than it sounds, and the reason is worth knowing:
/// in this kind of ERP a price does not belong to a customer, it belongs to a
/// <em>price class</em>, and customers are assigned to one. So "which customer
/// is cheapest" is really "which class is cheapest, and who is in it" — a
/// handful of quotes rather than one per account, however many accounts there
/// are.
/// </remarks>
/// <param name="catalogue">The part catalogue, for the list price and category.</param>
/// <param name="customers">The account file, for who exists and what class they are in.</param>
/// <param name="pricing">Contract terms, read per class and category.</param>
/// <param name="clock">Today, because terms expire.</param>
public sealed class PriceComparison(
    CatalogueProjection catalogue,
    CustomerDirectory customers,
    PricingReader pricing,
    TimeProvider clock)
{
    /// <summary>
    /// How many accounts to read when working out which classes are in play.
    ///
    /// A cap rather than the whole file, because this runs inside a question
    /// somebody is waiting on. Well past the demonstration's account count, and
    /// the reply says plainly when it has been reached rather than quietly
    /// comparing a subset.
    /// </summary>
    private const int AccountsScanned = 400;

    /// <summary>Example accounts named per class, so an answer can be specific.</summary>
    private const int ExamplesPerClass = 3;

    private readonly CatalogueProjection _catalogue = catalogue;
    private readonly CustomerDirectory _customers = customers;
    private readonly PricingReader _pricing = pricing;
    private readonly TimeProvider _clock = clock;

    /// <summary>
    /// Price one part for one customer.
    /// </summary>
    /// <param name="partNumber">The part.</param>
    /// <param name="account">The customer's account number.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <returns>The quote, or null when the part is unknown.</returns>
    public async Task<CustomerQuote?> ForCustomerAsync(
        string partNumber,
        string account,
        CancellationToken cancellationToken)
    {
        Part? part = _catalogue.Find(partNumber);
        if (part is null)
        {
            return null;
        }

        CustomerAccount customer;
        try
        {
            customer = await _pricing.ReadCustomerAsync(account, cancellationToken);
        }
        catch (ErpRecordNotFoundException)
        {
            return null;
        }

        QuotedPrice quoted = await QuoteForClassAsync(
            part, customer.PriceClass, cancellationToken);

        return new CustomerQuote(
            customer.AccountNumber,
            customer.Name,
            customer.PriceClass,
            quoted.ListPrice,
            quoted.NetPrice,
            quoted.AppliedTerms?.Multiplier,
            quoted.AppliedTerms?.Description ?? "no terms in force; quoted at list");
    }

    /// <summary>
    /// Every price class that buys this part, cheapest first.
    /// </summary>
    /// <param name="partNumber">The part.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <returns>The comparison, or null when the part is unknown.</returns>
    public async Task<PriceSpread?> AcrossCustomersAsync(
        string partNumber,
        CancellationToken cancellationToken)
    {
        Part? part = _catalogue.Find(partNumber);
        if (part is null)
        {
            return null;
        }

        (IReadOnlyList<CustomerAccount> accounts, int totalCount) =
            await _customers.BrowseAsync(AccountsScanned, cancellationToken);

        Dictionary<string, List<CustomerAccount>> byClass = [];
        foreach (CustomerAccount account in accounts)
        {
            if (!byClass.TryGetValue(account.PriceClass, out List<CustomerAccount>? members))
            {
                members = [];
                byClass[account.PriceClass] = members;
            }

            members.Add(account);
        }

        List<ClassPrice> priced = [];
        foreach ((string priceClass, List<CustomerAccount> members) in byClass)
        {
            QuotedPrice quoted = await QuoteForClassAsync(part, priceClass, cancellationToken);

            priced.Add(new ClassPrice(
                priceClass,
                quoted.NetPrice,
                quoted.AppliedTerms?.Multiplier,
                quoted.AppliedTerms?.Description ?? "no terms in force; quoted at list",
                members.Count,
                members.Take(ExamplesPerClass)
                    .Select(member => $"{member.Name} ({member.AccountNumber})")
                    .ToList()));
        }

        return new PriceSpread(
            part.PartNumber,
            part.Description,
            part.ListPrice,
            part.CategoryCode,
            accounts.Count,
            totalCount,
            [.. priced.OrderBy(entry => entry.NetPrice)]);
    }

    /// <summary>
    /// Quote a part against one price class.
    /// </summary>
    /// <param name="part">The part, for its list price and category.</param>
    /// <param name="priceClass">The class to price against.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    private async Task<QuotedPrice> QuoteForClassAsync(
        Part part,
        string priceClass,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContractTerms> terms = await _pricing.ReadTermsAsync(
            priceClass, part.CategoryCode, cancellationToken);

        return PriceCalculator.Quote(
            part.ListPrice,
            terms,
            DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
    }
}

/// <summary>One customer's price for one part.</summary>
/// <param name="Account">Their account number.</param>
/// <param name="Name">Their name.</param>
/// <param name="PriceClass">The class their price comes from.</param>
/// <param name="ListPrice">The undiscounted price.</param>
/// <param name="NetPrice">What they pay.</param>
/// <param name="Multiplier">The multiplier applied, if any.</param>
/// <param name="Terms">Which terms produced it, or why none did.</param>
public sealed record CustomerQuote(
    string Account,
    string Name,
    string PriceClass,
    decimal ListPrice,
    decimal NetPrice,
    decimal? Multiplier,
    string Terms);

/// <summary>What one price class pays for a part.</summary>
/// <param name="PriceClass">The class.</param>
/// <param name="NetPrice">What it pays.</param>
/// <param name="Multiplier">The multiplier applied, if any.</param>
/// <param name="Terms">Which terms produced it, or why none did.</param>
/// <param name="CustomerCount">How many scanned accounts are in this class.</param>
/// <param name="Examples">A few of them, named.</param>
public sealed record ClassPrice(
    string PriceClass,
    decimal NetPrice,
    decimal? Multiplier,
    string Terms,
    int CustomerCount,
    IReadOnlyList<string> Examples);

/// <summary>What a part costs across every class, cheapest first.</summary>
/// <param name="PartNumber">The part.</param>
/// <param name="Description">Its description.</param>
/// <param name="ListPrice">The undiscounted price.</param>
/// <param name="CategoryCode">The category the terms are written against.</param>
/// <param name="AccountsScanned">How many accounts were read.</param>
/// <param name="AccountsTotal">How many exist, so a partial scan says so.</param>
/// <param name="Classes">Every class found, cheapest first.</param>
public sealed record PriceSpread(
    string PartNumber,
    string Description,
    decimal ListPrice,
    string CategoryCode,
    int AccountsScanned,
    int AccountsTotal,
    IReadOnlyList<ClassPrice> Classes);
