namespace Counter.Domain.Availability;

/// <summary>
/// What one branch holds of one part.
/// </summary>
/// <remarks>
/// This type exists so that a branch and its quantities travel together. The ERP
/// stores them as separate parallel fields, and the moment they are carried
/// separately something can zip them wrongly — which produces a screen that looks
/// entirely correct while attributing one branch's stock to another.
/// </remarks>
/// <param name="BranchCode">The branch's code, as the inventory record names it.</param>
/// <param name="OnHand">Units physically present.</param>
/// <param name="Committed">Units promised to orders that hold stock.</param>
/// <param name="OnOrder">Units expected from a supplier. Not stock, and never counted as available.</param>
/// <param name="Bin">Where it sits in the warehouse, empty when unrecorded.</param>
public sealed record BranchPosition(
    string BranchCode,
    int OnHand,
    int Committed,
    int OnOrder,
    string Bin)
{
    /// <summary>
    /// Units that can be sold to the customer on the phone right now.
    /// </summary>
    /// <remarks>
    /// Clamped at zero. Committed genuinely does exceed on-hand in real ERP data —
    /// usually where an order was closed without releasing its allocation — and a
    /// negative quantity on a screen is nonsense a representative cannot act on.
    /// </remarks>
    public int FreeToSell => Math.Max(0, OnHand - Committed);

    /// <summary>
    /// How this branch's position should read at a glance.
    /// </summary>
    public StockState State => this switch
    {
        { FreeToSell: > 0 } => StockState.Available,
        { OnHand: > 0 } => StockState.AllCommitted,
        _ => StockState.None,
    };
}
