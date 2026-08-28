namespace Counter.Domain.Orders;

/// <summary>
/// Where an order stands, and therefore whether it holds stock.
/// </summary>
/// <remarks>
/// The set is closed. An unrecognised value must hold no stock rather than being
/// counted as though it did: a typo in ERP data would otherwise inflate what
/// appears committed and understate what a representative can sell.
/// </remarks>
public enum OrderState
{
    /// <summary>Priced but not promised. Holds nothing.</summary>
    Quote,

    /// <summary>The customer has committed. Holds stock.</summary>
    Confirmed,

    /// <summary>Stock is earmarked for it. Holds stock.</summary>
    Allocated,

    /// <summary>Being picked from the shelf. Holds stock.</summary>
    Picking,

    /// <summary>The stock has left. Holds nothing.</summary>
    Shipped,

    /// <summary>Never taken. Holds nothing.</summary>
    Cancelled,

    /// <summary>
    /// A value the ERP holds that this application does not recognise. Holds
    /// nothing, deliberately: the safe reading of an unknown state is that it
    /// makes no claim on stock.
    /// </summary>
    Unrecognised,
}

/// <summary>Reading order states from what the ERP stores.</summary>
public static class OrderStates
{
    /// <summary>The states that hold stock against a branch's on-hand quantity.</summary>
    public static readonly IReadOnlySet<OrderState> HoldingStock =
        new HashSet<OrderState> { OrderState.Confirmed, OrderState.Allocated, OrderState.Picking };

    /// <summary>
    /// Read a stored state, returning <see cref="OrderState.Unrecognised"/> for
    /// anything not in the closed set.
    /// </summary>
    /// <param name="stored">The value as the ERP holds it.</param>
    public static OrderState Parse(string? stored) => stored?.Trim().ToUpperInvariant() switch
    {
        "QUOTE" => OrderState.Quote,
        "CONFIRMED" => OrderState.Confirmed,
        "ALLOCATED" => OrderState.Allocated,
        "PICKING" => OrderState.Picking,
        "SHIPPED" => OrderState.Shipped,
        "CANCELLED" => OrderState.Cancelled,
        _ => OrderState.Unrecognised,
    };

    /// <summary>Whether a state holds stock.</summary>
    /// <param name="state">The state to test.</param>
    public static bool HoldsStock(this OrderState state) => HoldingStock.Contains(state);
}
