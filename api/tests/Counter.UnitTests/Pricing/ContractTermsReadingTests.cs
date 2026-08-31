namespace Counter.UnitTests.Pricing;

using Counter.Domain.Pricing;
using Counter.Infrastructure.Erp;

/// <summary>
/// Reading contract terms out of a PRICING record.
/// </summary>
/// <remarks>
/// A PRICING record holds parallel fields: position n of the multiplier list,
/// the from-date list and the to-date list are one agreement. Pairing a
/// multiplier with another agreement's dates would apply a discount for a
/// period it was never agreed for, which is a billing error rather than a
/// display one.
///
/// The record used here is a real one from the demonstration data, with two
/// agreements of which one has expired.
/// </remarks>
public sealed class ContractTermsReadingTests
{
    /// <summary>The stored form of A1*BRK: two agreements, parallel fields.</summary>
    private const string TwoAgreements =
        "0.70ý0.53þ2026-01-03ý2026-02-12þ2026-12-29ý2026-07-22";

    [Fact]
    public void Both_agreements_are_read_with_their_own_dates()
    {
        IReadOnlyList<ContractTerms> terms =
            PricingReader.ReadTerms(TwoAgreements, "A1", "BRK");

        Assert.Equal(2, terms.Count);
        Assert.Equal(0.70m, terms[0].Multiplier);
        Assert.Equal(new DateOnly(2026, 1, 3), terms[0].EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 12, 29), terms[0].EffectiveTo);
        Assert.Equal(0.53m, terms[1].Multiplier);
        Assert.Equal(new DateOnly(2026, 7, 22), terms[1].EffectiveTo);
    }

    [Fact]
    public void The_agreement_in_force_today_is_the_one_that_prices()
    {
        // 2026-08-31 sits inside the first agreement and past the second.
        QuotedPrice quoted = PriceCalculator.Quote(
            409.26m,
            PricingReader.ReadTerms(TwoAgreements, "A1", "BRK"),
            new DateOnly(2026, 8, 31));

        Assert.Equal(PriceBasis.Contract, quoted.Basis);
        Assert.Equal(0.70m, quoted.AppliedTerms!.Multiplier);
        Assert.Equal(286.48m, Math.Round(quoted.NetPrice, 2));
    }

    [Fact]
    public void An_expired_agreement_is_named_rather_than_silently_dropped()
    {
        // So an unexpectedly high price can be explained on the call rather
        // than escalated: "that discount ended in July" is an answer.
        QuotedPrice quoted = PriceCalculator.Quote(
            409.26m,
            PricingReader.ReadTerms(TwoAgreements, "A1", "BRK"),
            new DateOnly(2026, 8, 31));

        Assert.Single(quoted.DisregardedTerms);
    }

    [Fact]
    public void A_multiplier_with_no_matching_dates_is_skipped()
    {
        // The parallel-field hazard. A third multiplier with only two date
        // pairs must not borrow the second agreement's window.
        string ragged =
            "0.70ý0.53ý0.40þ2026-01-03ý2026-02-12þ2026-12-29ý2026-07-22";

        IReadOnlyList<ContractTerms> terms =
            PricingReader.ReadTerms(ragged, "A1", "BRK");

        Assert.Equal(2, terms.Count);
        Assert.DoesNotContain(terms, term => term.Multiplier == 0.40m);
    }

    [Fact]
    public void A_single_agreement_record_reads_as_one()
    {
        // Most records look like this: one multiplier, no value marks at all.
        IReadOnlyList<ContractTerms> terms = PricingReader.ReadTerms(
            "0.70þ2026-01-03þ2026-12-29", "A1", "BOX");

        Assert.Single(terms);
        Assert.Equal(0.70m, terms[0].Multiplier);
    }
}
