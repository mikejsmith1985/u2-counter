namespace Counter.UnitTests;

using Counter.Domain.Orders;

/// <summary>
/// Whether the orders holding a branch's stock account for all of it.
/// </summary>
public sealed class CommitmentTests
{
    private static Commitment Holding(string orderNumber, int quantity) =>
        new(
            orderNumber,
            "C-10442",
            "Front Range Electric",
            quantity,
            OrderState.Allocated,
            new DateOnly(2026, 9, 2));

    [Fact]
    public void Listed_commitments_are_summed()
    {
        BranchCommitments branch = new("DEN", 40, [Holding("SO-1", 25), Holding("SO-2", 15)]);

        Assert.Equal(40, branch.AccountedFor);
    }

    [Fact]
    public void Nothing_is_unaccounted_when_the_orders_add_up()
    {
        BranchCommitments branch = new("DEN", 40, [Holding("SO-1", 25), Holding("SO-2", 15)]);

        Assert.Equal(0, branch.Unaccounted);
    }

    [Fact]
    public void A_shortfall_is_reported_rather_than_hidden()
    {
        // This happens in real systems: an order closed without releasing its
        // allocation leaves stock committed to nothing. A discrepancy someone can
        // see is worth more than a screen that quietly adds up.
        BranchCommitments branch = new("DEN", 40, [Holding("SO-1", 25)]);

        Assert.Equal(15, branch.Unaccounted);
    }

    [Fact]
    public void Orders_exceeding_the_committed_total_do_not_produce_a_negative()
    {
        BranchCommitments branch = new("DEN", 10, [Holding("SO-1", 25)]);

        Assert.Equal(0, branch.Unaccounted);
    }

    [Fact]
    public void A_branch_with_no_commitments_accounts_for_nothing()
    {
        BranchCommitments branch = new("DEN", 0, []);

        Assert.Equal(0, branch.AccountedFor);
        Assert.Equal(0, branch.Unaccounted);
    }
}
