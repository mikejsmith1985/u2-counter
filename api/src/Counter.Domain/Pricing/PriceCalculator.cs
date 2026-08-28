namespace Counter.Domain.Pricing;

/// <summary>
/// Turns a list price and a customer's terms into the price they pay.
/// </summary>
public static class PriceCalculator
{
    /// <summary>Prices are quoted to the cent.</summary>
    private const int CurrencyDecimalPlaces = 2;

    /// <summary>
    /// Work out what a customer pays, and record what did not apply.
    /// </summary>
    /// <param name="listPrice">The undiscounted catalogue price.</param>
    /// <param name="availableTerms">Every set of terms covering this customer and category.</param>
    /// <param name="on">The day to price for, normally today.</param>
    /// <returns>The price, with the terms that produced it and those that did not.</returns>
    /// <remarks>
    /// Where several sets of terms are in force the lowest multiplier wins: the
    /// customer gets their best agreed price, which is what they would expect and
    /// what a representative would honour anyway.
    /// </remarks>
    public static QuotedPrice Quote(
        decimal listPrice,
        IReadOnlyList<ContractTerms> availableTerms,
        DateOnly on)
    {
        ArgumentNullException.ThrowIfNull(availableTerms);

        List<ContractTerms> applicable = availableTerms.Where(terms => terms.AppliesOn(on)).ToList();
        List<DisregardedTerms> disregarded = availableTerms
            .Where(terms => !terms.AppliesOn(on))
            .Select(terms => new DisregardedTerms(terms, DescribeWhyNotApplied(terms, on)))
            .ToList();

        if (applicable.Count == 0)
        {
            return new QuotedPrice(
                listPrice,
                Round(listPrice),
                PriceBasis.List,
                AppliedTerms: null,
                disregarded);
        }

        ContractTerms best = applicable.MinBy(terms => terms.Multiplier)!;

        // Terms that were in force but beaten by a better rate are also worth
        // reporting: a representative asked why a promotion did not apply should
        // be able to say it did not beat the standing agreement.
        disregarded.AddRange(
            applicable
                .Where(terms => !ReferenceEquals(terms, best))
                .Select(terms => new DisregardedTerms(
                    terms,
                    $"A better rate applied: {best.Multiplier:0.00} beats {terms.Multiplier:0.00}")));

        return new QuotedPrice(
            listPrice,
            Round(listPrice * best.Multiplier),
            PriceBasis.Contract,
            best,
            disregarded);
    }

    /// <summary>
    /// Explain, in words a representative can repeat, why terms did not apply.
    /// </summary>
    private static string DescribeWhyNotApplied(ContractTerms terms, DateOnly on)
    {
        if (on > terms.EffectiveTo)
        {
            return $"Expired on {terms.EffectiveTo:yyyy-MM-dd}";
        }

        return $"Not yet in effect; begins {terms.EffectiveFrom:yyyy-MM-dd}";
    }

    /// <summary>Round to the cent, away from zero, as currency is quoted.</summary>
    private static decimal Round(decimal amount) =>
        Math.Round(amount, CurrencyDecimalPlaces, MidpointRounding.AwayFromZero);
}
