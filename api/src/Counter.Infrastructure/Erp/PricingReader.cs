namespace Counter.Infrastructure.Erp;

using Counter.Domain.Pricing;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;

/// <summary>
/// Reads customers and the contract terms that apply to them.
/// </summary>
public sealed class PricingReader(IErpReader erp)
{
    private readonly IErpReader _erp = erp;

    /// <summary>
    /// Read a customer account.
    /// </summary>
    /// <param name="account">The account number.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <exception cref="ErpRecordNotFoundException">If no such account exists.</exception>
    public async Task<CustomerAccount> ReadCustomerAsync(
        string account,
        CancellationToken cancellationToken)
    {
        string raw = await _erp.ReadRecordAsync(
            ErpFiles.Customer.Name, account, cancellationToken);

        return ReadCustomer(account, raw);
    }

    /// <summary>
    /// Read the terms covering one customer and one category.
    /// </summary>
    /// <param name="priceClass">The customer's price class.</param>
    /// <param name="categoryCode">The part's category.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <returns>
    /// Every set of terms on file, in force or not. Terms that do not apply are
    /// returned too, because the caller must be able to say why a price is what
    /// it is — an expired promotion is exactly when someone asks.
    /// </returns>
    public async Task<IReadOnlyList<ContractTerms>> ReadTermsAsync(
        string priceClass,
        string categoryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(priceClass) || string.IsNullOrWhiteSpace(categoryCode))
        {
            return [];
        }

        try
        {
            string raw = await _erp.ReadRecordAsync(
                ErpFiles.Pricing.Name,
                ErpFiles.Pricing.KeyFor(priceClass, categoryCode),
                cancellationToken);

            return ReadTerms(raw, priceClass, categoryCode);
        }
        catch (ErpRecordNotFoundException)
        {
            // No terms on file simply means this customer pays list for this
            // category, which is an ordinary answer rather than a failure.
            return [];
        }
    }

    /// <summary>Build a customer from its stored record.</summary>
    public static CustomerAccount ReadCustomer(string account, string raw)
    {
        MultiValueRecord record = MultiValueRecord.Parse(raw);

        return new CustomerAccount(
            AccountNumber: account,
            Name: record.Field(ErpFiles.Customer.CustomerName),
            AddressLines: record.Values(ErpFiles.Customer.AddressLines),
            PaymentTerms: record.Field(ErpFiles.Customer.PaymentTerms),
            PriceClass: record.Field(ErpFiles.Customer.PriceClass),
            HomeBranchCode: record.Field(ErpFiles.Customer.HomeBranch));
    }

    /// <summary>
    /// Build every set of terms from a pricing record's parallel fields.
    /// </summary>
    /// <remarks>
    /// Position n of each field belongs to the same agreement, so a multiplier
    /// without matching dates is skipped rather than paired with another
    /// agreement's window — which would apply a discount for the wrong period.
    /// </remarks>
    public static IReadOnlyList<ContractTerms> ReadTerms(
        string raw,
        string priceClass,
        string categoryCode)
    {
        MultiValueRecord record = MultiValueRecord.Parse(raw);

        IReadOnlyList<string> multipliers = record.Values(ErpFiles.Pricing.Multipliers);
        IReadOnlyList<string> from = record.Values(ErpFiles.Pricing.EffectiveFrom);
        IReadOnlyList<string> to = record.Values(ErpFiles.Pricing.EffectiveTo);

        List<ContractTerms> terms = [];

        for (int position = 0; position < multipliers.Count; position++)
        {
            if (position >= from.Count || position >= to.Count)
            {
                continue;
            }

            if (!decimal.TryParse(multipliers[position], out decimal multiplier) ||
                !DateOnly.TryParse(from[position], out DateOnly effectiveFrom) ||
                !DateOnly.TryParse(to[position], out DateOnly effectiveTo))
            {
                continue;
            }

            terms.Add(new ContractTerms(
                multiplier,
                effectiveFrom,
                effectiveTo,
                $"Price class {priceClass}, category {categoryCode}, " +
                $"{effectiveFrom:yyyy-MM-dd} to {effectiveTo:yyyy-MM-dd}"));
        }

        return terms;
    }
}

/// <summary>
/// A customer, as much of one as pricing and display require.
/// </summary>
/// <param name="AccountNumber">The key.</param>
/// <param name="Name">Trading name.</param>
/// <param name="AddressLines">Address, one entry per line.</param>
/// <param name="PaymentTerms">How they settle.</param>
/// <param name="PriceClass">Joins to contract pricing; empty when they pay list.</param>
/// <param name="HomeBranchCode">Where they normally collect from.</param>
public sealed record CustomerAccount(
    string AccountNumber,
    string Name,
    IReadOnlyList<string> AddressLines,
    string PaymentTerms,
    string PriceClass,
    string HomeBranchCode);
