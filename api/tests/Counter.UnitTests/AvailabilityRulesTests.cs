namespace Counter.UnitTests;

using Counter.Domain.Availability;
using Counter.Domain.Catalogue;

/// <summary>
/// How free-to-sell is worked out, and how a branch's position reads.
/// </summary>
/// <remarks>
/// These rules decide what a representative tells a customer, so each is asserted
/// on its own rather than through a screen that might mask it.
/// </remarks>
public sealed class AvailabilityRulesTests
{
    private static BranchPosition Position(int onHand, int committed, int onOrder = 0) =>
        new("DEN", onHand, committed, onOrder, "A-12");

    [Fact]
    public void FreeToSell_is_on_hand_less_committed()
    {
        Assert.Equal(102, Position(onHand: 142, committed: 40).FreeToSell);
    }

    [Fact]
    public void FreeToSell_is_zero_when_everything_is_committed()
    {
        Assert.Equal(0, Position(onHand: 60, committed: 60).FreeToSell);
    }

    [Fact]
    public void FreeToSell_never_goes_negative()
    {
        // Real ERP data does produce committed exceeding on-hand, usually where an
        // order was closed without releasing its allocation. A negative quantity
        // on screen is nonsense a representative cannot act on.
        Assert.Equal(0, Position(onHand: 10, committed: 25).FreeToSell);
    }

    [Fact]
    public void Stock_on_order_from_a_supplier_is_not_available()
    {
        // It is a promise from a supplier, not stock on a shelf. Counting it would
        // have a representative promise a customer something nobody has yet.
        Assert.Equal(0, Position(onHand: 0, committed: 0, onOrder: 200).FreeToSell);
    }

    [Fact]
    public void A_branch_with_stock_free_reads_as_available()
    {
        Assert.Equal(StockState.Available, Position(onHand: 142, committed: 40).State);
    }

    [Fact]
    public void A_branch_whose_stock_is_all_promised_reads_as_all_committed()
    {
        // Distinct from None, because "we have twelve but they are spoken for"
        // invites a conversation about when they free up.
        Assert.Equal(StockState.AllCommitted, Position(onHand: 60, committed: 60).State);
    }

    [Fact]
    public void A_branch_with_no_stock_reads_as_none()
    {
        Assert.Equal(StockState.None, Position(onHand: 0, committed: 0).State);
    }

    [Fact]
    public void An_oversold_branch_reads_as_all_committed_not_none()
    {
        // There is stock on the shelf; it is simply all promised.
        Assert.Equal(StockState.AllCommitted, Position(onHand: 10, committed: 25).State);
    }

    [Fact]
    public void Total_free_to_sell_sums_every_branch()
    {
        PartAvailability availability = Availability(
            new BranchPosition("DEN", 142, 40, 0, "A-12"),
            new BranchPosition("AUR", 38, 12, 0, "C-04"),
            new BranchPosition("BOU", 0, 0, 200, string.Empty));

        Assert.Equal(128, availability.TotalFreeToSell);
    }

    [Fact]
    public void Branches_with_stock_are_ordered_by_how_much_they_have()
    {
        PartAvailability availability = Availability(
            new BranchPosition("DEN", 20, 0, 0, "A-12"),
            new BranchPosition("AUR", 90, 0, 0, "C-04"),
            new BranchPosition("BOU", 0, 0, 0, string.Empty));

        Assert.Equal(["AUR", "DEN"], availability.BranchesWithStock.Select(p => p.BranchCode));
    }

    [Fact]
    public void A_part_with_no_inventory_record_reports_stock_as_unknown()
    {
        // Unknown is not zero. Telling a customer there is none when nobody has
        // counted is a different and worse answer.
        PartAvailability availability = new(SamplePart, [], IsStockKnown: false);

        Assert.False(availability.IsStockKnown);
        Assert.Empty(availability.Positions);
    }

    private static PartAvailability Availability(params BranchPosition[] positions) =>
        new(SamplePart, positions, IsStockKnown: true);

    private static Part SamplePart => new(
        "SQD-QO120",
        "QO 20A 1-Pole Circuit Breaker",
        "Square D",
        "QO120",
        "EA",
        "BRK",
        12.40m,
        IsDiscontinued: false);
}
