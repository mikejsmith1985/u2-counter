namespace Counter.UnitTests;

using Counter.Domain.Pricing;

/// <summary>
/// How a customer's price is worked out, and what is reported about terms that
/// did not apply.
/// </summary>
public sealed class PricingRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 28);

    private static ContractTerms Terms(
        decimal multiplier,
        string from,
        string to,
        string description = "Standing agreement") =>
        new(multiplier, DateOnly.Parse(from), DateOnly.Parse(to), description);

    [Fact]
    public void With_no_terms_the_list_price_is_quoted()
    {
        QuotedPrice quoted = PriceCalculator.Quote(12.40m, [], Today);

        Assert.Equal(12.40m, quoted.NetPrice);
        Assert.Equal(PriceBasis.List, quoted.Basis);
    }

    [Fact]
    public void With_no_terms_no_agreement_is_reported_as_applied()
    {
        // A representative must be able to say "that is list" rather than
        // implying a discount was applied.
        Assert.Null(PriceCalculator.Quote(12.40m, [], Today).AppliedTerms);
    }

    [Fact]
    public void Current_terms_produce_the_net_price()
    {
        QuotedPrice quoted = PriceCalculator.Quote(
            12.40m,
            [Terms(0.70m, "2026-01-01", "2026-12-31")],
            Today);

        Assert.Equal(8.68m, quoted.NetPrice);
        Assert.Equal(PriceBasis.Contract, quoted.Basis);
    }

    [Fact]
    public void Expired_terms_are_not_applied()
    {
        QuotedPrice quoted = PriceCalculator.Quote(
            12.40m,
            [Terms(0.50m, "2026-01-01", "2026-06-30", "Spring promotion")],
            Today);

        Assert.Equal(PriceBasis.List, quoted.Basis);
        Assert.Equal(12.40m, quoted.NetPrice);
    }

    [Fact]
    public void Expired_terms_are_reported_with_a_reason()
    {
        // This is the case where a price looks unexpectedly high and someone has
        // to explain it while the customer waits.
        QuotedPrice quoted = PriceCalculator.Quote(
            12.40m,
            [Terms(0.50m, "2026-01-01", "2026-06-30", "Spring promotion")],
            Today);

        Assert.Single(quoted.DisregardedTerms);
        Assert.Contains("Expired on 2026-06-30", quoted.DisregardedTerms[0].Reason);
    }

    [Fact]
    public void Terms_not_yet_in_effect_are_not_applied()
    {
        QuotedPrice quoted = PriceCalculator.Quote(
            12.40m,
            [Terms(0.50m, "2026-11-01", "2026-12-31", "Winter deal")],
            Today);

        Assert.Equal(PriceBasis.List, quoted.Basis);
        Assert.Contains("Not yet in effect", quoted.DisregardedTerms[0].Reason);
    }

    [Fact]
    public void The_best_rate_wins_when_several_apply()
    {
        // The customer gets their best agreed price, which is what a
        // representative would honour anyway.
        QuotedPrice quoted = PriceCalculator.Quote(
            100.00m,
            [Terms(0.70m, "2026-01-01", "2026-12-31"), Terms(0.62m, "2026-08-01", "2026-09-30")],
            Today);

        Assert.Equal(62.00m, quoted.NetPrice);
    }

    [Fact]
    public void Terms_beaten_by_a_better_rate_are_reported_too()
    {
        // Answers the question "why did the promotion not apply?" — because the
        // standing agreement beat it.
        QuotedPrice quoted = PriceCalculator.Quote(
            100.00m,
            [Terms(0.70m, "2026-01-01", "2026-12-31"), Terms(0.62m, "2026-08-01", "2026-09-30")],
            Today);

        Assert.Single(quoted.DisregardedTerms);
        Assert.Contains("better rate", quoted.DisregardedTerms[0].Reason);
    }

    [Fact]
    public void Prices_are_rounded_to_the_cent()
    {
        QuotedPrice quoted = PriceCalculator.Quote(
            9.99m,
            [Terms(0.6667m, "2026-01-01", "2026-12-31")],
            Today);

        Assert.Equal(6.66m, quoted.NetPrice);
    }

    [Fact]
    public void Terms_effective_only_today_are_applied()
    {
        // Boundary: the first and last day of a window are inside it.
        QuotedPrice quoted = PriceCalculator.Quote(
            100.00m,
            [Terms(0.80m, "2026-08-28", "2026-08-28")],
            Today);

        Assert.Equal(80.00m, quoted.NetPrice);
    }

    [Fact]
    public void The_list_price_travels_with_the_net_price()
    {
        // The workings travel with the answer, so the discount is visible and a
        // representative can explain it without escalating.
        QuotedPrice quoted = PriceCalculator.Quote(
            12.40m,
            [Terms(0.70m, "2026-01-01", "2026-12-31")],
            Today);

        Assert.Equal(12.40m, quoted.ListPrice);
        Assert.Equal(0.70m, quoted.AppliedTerms!.Multiplier);
    }
}
