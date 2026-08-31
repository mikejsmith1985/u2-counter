namespace Counter.UnitTests.Ai;

using Counter.Infrastructure.Ai;

/// <summary>
/// What a price comparison says to the model that reads it.
/// </summary>
/// <remarks>
/// What a tool hands over is what the model believes, and the one defect this
/// project has actually shipped in that space was of exactly this kind: a tool
/// returned a list of branches with no total, so the model added them up itself
/// and answered twenty units high. Every branch it quoted was right.
///
/// Two things here carry the same risk. A comparison that is merely ordered
/// rather than answered leaves the model to work out that the first line was
/// the point, which is the work the tool already did. And "cheapest" over a
/// subset of the account file is a different claim from "cheapest" -- the two
/// read identically by the time the answer reaches a person, so the subset has
/// to be said out loud or not scanned at all.
/// </remarks>
public sealed class PriceSpreadWordingTests
{
    /// <summary>One price class and what it pays.</summary>
    /// <param name="priceClass">The class.</param>
    /// <param name="netPrice">What it pays.</param>
    /// <param name="multiplier">The multiplier applied, or null for none.</param>
    /// <param name="customerCount">How many scanned accounts are in it.</param>
    /// <returns>The class price.</returns>
    private static ClassPrice Class(
        string priceClass,
        decimal netPrice,
        decimal? multiplier,
        int customerCount = 2) =>
        new(
            priceClass,
            netPrice,
            multiplier,
            multiplier is null ? "no terms in force; quoted at list" : "terms in force",
            customerCount,
            [$"Front Range Electric (C-1000{priceClass[^1]})"]);

    /// <summary>A comparison over a whole account file.</summary>
    /// <param name="classes">The classes, cheapest first.</param>
    /// <param name="scanned">How many accounts were read.</param>
    /// <param name="onFile">How many exist.</param>
    /// <returns>The spread.</returns>
    private static PriceSpread Spread(
        IReadOnlyList<ClassPrice> classes,
        int scanned = 150,
        int onFile = 150) =>
        new("E-BRK00008", "15A AFCI Breaker", 409.26m, "BRK", scanned, onFile, classes);

    /// <summary>Three classes at three prices, cheapest first.</summary>
    /// <returns>The spread.</returns>
    private static PriceSpread ThreeClasses() =>
        Spread([
            Class("B2", 241.46m, 0.59m),
            Class("A1", 286.48m, 0.70m),
            Class("D1", 409.26m, null),
        ]);

    [Fact]
    public void The_cheapest_class_is_named_as_the_answer_not_left_first_in_a_list()
    {
        // Ordered is not answered. A wall of near-identical lines makes the
        // reader redo the comparison the tool already performed.
        string described = AskService.DescribeSpread(ThreeClasses());

        Assert.Contains("CHEAPEST -- class B2 at 241.46", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_other_class_says_how_much_more_it_pays()
    {
        // The difference is the useful half of the answer, and it is arithmetic
        // the model should not be left to do.
        string described = AskService.DescribeSpread(ThreeClasses());

        Assert.Contains("class A1 at 286.48 (+45.02)", described, StringComparison.Ordinal);
        Assert.Contains("class D1 at 409.26 (+167.80)", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_with_no_terms_says_so_rather_than_showing_a_blank_multiplier()
    {
        // Silence would read as a missing figure, which is a different thing
        // from a class that simply pays list.
        string described = AskService.DescribeSpread(ThreeClasses());

        Assert.Contains("no terms in force", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_scan_is_said_out_loud()
    {
        // The important one. "Cheapest" over 150 of 400 accounts is a different
        // claim from "cheapest", and by the time it reaches a person the two
        // are the same sentence.
        string described = AskService.DescribeSpread(
            Spread([Class("A1", 286.48m, 0.70m)], scanned: 150, onFile: 400));

        Assert.Contains("Read 150 of 400 accounts", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_complete_scan_does_not_claim_to_be_partial()
    {
        // The other direction. A warning on every answer is a warning nobody
        // reads, and the one time it matters it would be ignored.
        string described = AskService.DescribeSpread(ThreeClasses());

        Assert.DoesNotContain("accounts;", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_class_is_still_named_as_the_cheapest_with_nothing_to_compare()
    {
        // One class is a legitimate answer -- everybody buys on the same terms.
        // It must not produce an empty "every other class" heading.
        string described = AskService.DescribeSpread(Spread([Class("A1", 286.48m, 0.70m)]));

        Assert.Contains("CHEAPEST -- class A1", described, StringComparison.Ordinal);
        Assert.DoesNotContain("Every other class", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_part_nobody_has_terms_for_still_reports_the_part_and_its_list_price()
    {
        // No classes at all. The answer is that everybody pays list, and the
        // list price has to be in it or there is no answer.
        string described = AskService.DescribeSpread(Spread([]));

        Assert.Contains("E-BRK00008", described, StringComparison.Ordinal);
        Assert.Contains("list 409.26", described, StringComparison.Ordinal);
        Assert.DoesNotContain("CHEAPEST", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_category_the_terms_are_written_against_is_named()
    {
        // Contract terms join on the category, so it is the field somebody
        // checks when a price looks wrong.
        Assert.Contains(
            "category BRK", AskService.DescribeSpread(ThreeClasses()), StringComparison.Ordinal);
    }

    [Fact]
    public void Accounts_are_named_so_the_answer_can_be_checked()
    {
        // "Class B2 is cheapest" cannot be acted on. A name and an account
        // number can.
        string described = AskService.DescribeSpread(ThreeClasses());

        Assert.Contains("including Front Range Electric", described, StringComparison.Ordinal);
    }
}
