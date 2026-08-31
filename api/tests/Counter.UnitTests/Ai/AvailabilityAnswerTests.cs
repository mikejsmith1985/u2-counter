namespace Counter.UnitTests.Ai;

using Counter.Domain.Availability;
using Counter.Domain.Catalogue;
using Counter.Infrastructure.Ai;

/// <summary>
/// What the assistant is told about stock, before it says anything about it.
/// </summary>
/// <remarks>
/// These exist because of a wrong answer that every existing test passed. Asked
/// for a total across every branch, the assistant listed six branches with the
/// right figure against each and then gave a total twenty units too high. The
/// request succeeded, the tool call succeeded, the cited record was the right
/// one, and the answer was wrong -- which is the shape of failure a suite that
/// checks status codes cannot see.
///
/// The cause was that the tool handed over a list and no total, so the only
/// place the sum could be worked out was in the model's head. These tests pin
/// the fix: the totals are stated, so the arithmetic is not the model's to do.
/// </remarks>
public sealed class AvailabilityAnswerTests
{
    /// <summary>A part, since these tests care only about the quantities.</summary>
    private static readonly Part AnyPart = new(
        "E-BRK00008", "20A AFCI Breaker", "Square D", "HOM120AFI",
        "EA", "BRK", 48.95m, false);

    /// <summary>Build a position at one branch.</summary>
    /// <param name="branchCode">The branch.</param>
    /// <param name="onHand">Units present.</param>
    /// <param name="committed">Units promised.</param>
    /// <returns>The position.</returns>
    private static BranchPosition At(string branchCode, int onHand, int committed) =>
        new(branchCode, onHand, committed, 0, string.Empty);

    /// <summary>The six branches from the answer that was wrong.</summary>
    /// <returns>A part's stock, totalling 373 free to sell.</returns>
    private static PartAvailability TheAnswerThatWasWrong() =>
        new(
            AnyPart,
            [
                At("LKW", 20, 4), At("GRE", 18, 2), At("PUE", 16, 2),
                At("FTC", 20, 4), At("WMR", 14, 2), At("GRJ", 310, 11),
            ],
            true);

    [Fact]
    public void The_total_free_to_sell_is_stated_rather_than_left_to_be_added_up()
    {
        // The specific defect. 16+16+14+16+12+299 is 373; the model said 393.
        PartAvailability stock = TheAnswerThatWasWrong();

        string described = AskService.DescribeAvailability(stock, AnyPart.PartNumber);

        Assert.Equal(373, stock.TotalFreeToSell);
        Assert.Contains("free to sell 373", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_total_on_hand_is_stated_too()
    {
        // Asked how many exist rather than how many are sellable, the model
        // would otherwise be adding a second column by hand.
        PartAvailability stock = TheAnswerThatWasWrong();

        string described = AskService.DescribeAvailability(stock, AnyPart.PartNumber);

        Assert.Equal(398, stock.TotalOnHand);
        Assert.Contains("on hand 398", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_branch_still_appears_with_its_own_figures()
    {
        // The totals are an addition, not a replacement: "which branch has the
        // most" is the more common question, and it needs the lines.
        string described = AskService.DescribeAvailability(
            TheAnswerThatWasWrong(), AnyPart.PartNumber);

        Assert.Contains("GRJ | on hand 310 | committed 11 | free to sell 299", described, StringComparison.Ordinal);
        Assert.Contains("WMR | on hand 14 | committed 2 | free to sell 12", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_is_told_to_use_the_stated_total()
    {
        // Without this the totals are just two more numbers in a list, and the
        // model may still prefer its own sum.
        string described = AskService.DescribeAvailability(
            TheAnswerThatWasWrong(), AnyPart.PartNumber);

        Assert.Contains("rather than adding the lines up", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_part_with_no_inventory_record_says_so_without_totals()
    {
        // "Nobody has counted it" and "there are none" are different answers,
        // and a total of zero would state the second when the first is true.
        PartAvailability none = new(AnyPart, [], false);

        string described = AskService.DescribeAvailability(none, AnyPart.PartNumber);

        Assert.Contains("No inventory record exists", described, StringComparison.Ordinal);
        Assert.DoesNotContain("free to sell 0", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Committed_above_on_hand_does_not_make_a_negative_total()
    {
        // Real ERP data does this, usually where an order closed without
        // releasing its allocation. A negative total is nonsense to read out.
        PartAvailability odd = new(AnyPart, [At("FTC", 3, 9), At("GRE", 10, 1)], true);

        string described = AskService.DescribeAvailability(odd, AnyPart.PartNumber);

        Assert.Equal(9, odd.TotalFreeToSell);
        Assert.Contains("free to sell 9", described, StringComparison.Ordinal);
    }
}
