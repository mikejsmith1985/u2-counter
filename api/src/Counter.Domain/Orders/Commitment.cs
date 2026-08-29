namespace Counter.Domain.Orders;

/// <summary>
/// One order line holding stock at a branch.
/// </summary>
/// <param name="OrderNumber">The order's key.</param>
/// <param name="CustomerAccount">Whose order it is.</param>
/// <param name="CustomerName">Their name, for reading aloud.</param>
/// <param name="Quantity">Units held.</param>
/// <param name="State">The order's state, which is why it holds them.</param>
/// <param name="PromisedDate">When the customer expects it.</param>
public sealed record Commitment(
    string OrderNumber,
    string CustomerAccount,
    string CustomerName,
    int Quantity,
    OrderState State,
    DateOnly PromisedDate);

/// <summary>
/// What is holding a branch's committed stock, and whether it adds up.
/// </summary>
/// <param name="BranchCode">The branch.</param>
/// <param name="CommittedTotal">What the inventory record says is committed.</param>
/// <param name="Commitments">The orders found to be holding it.</param>
/// <param name="WasCapped">
/// True when the search for orders hit its limit, so orders exist that this list
/// does not show.
///
/// It matters because of what it does to <see cref="Unaccounted"/>: orders that
/// were not read cannot account for anything, so the shortfall reads larger than
/// it is. That figure is the one a representative rings the branch about, and
/// sending them after a discrepancy that is really a truncated list wastes
/// somebody's afternoon and costs the screen its credibility.
/// </param>
public sealed record BranchCommitments(
    string BranchCode,
    int CommittedTotal,
    IReadOnlyList<Commitment> Commitments,
    bool WasCapped = false)
{
    /// <summary>Units the listed orders account for.</summary>
    public int AccountedFor => Commitments.Sum(commitment => commitment.Quantity);

    /// <summary>
    /// Committed units no listed order explains.
    /// </summary>
    /// <remarks>
    /// Reported rather than hidden. This happens in real systems — an order closed
    /// without releasing its allocation leaves stock committed to nothing — and a
    /// discrepancy someone can see is worth far more than a screen that quietly
    /// adds up.
    ///
    /// Not to be read as a discrepancy when <see cref="WasCapped"/> is true: the
    /// orders that would have explained it may simply not have been read.
    /// </remarks>
    public int Unaccounted => Math.Max(0, CommittedTotal - AccountedFor);
}
