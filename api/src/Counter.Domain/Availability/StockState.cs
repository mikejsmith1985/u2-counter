namespace Counter.Domain.Availability;

/// <summary>
/// How a branch's stock position reads to someone answering a phone call.
/// </summary>
/// <remarks>
/// Three states rather than a boolean, because "we have twelve but they are all
/// spoken for" is a different answer to a customer than "we have none". The first
/// invites a conversation about when they free up; the second does not.
/// </remarks>
public enum StockState
{
    /// <summary>Units are free to sell now.</summary>
    Available,

    /// <summary>Units are present but every one is promised to an order.</summary>
    AllCommitted,

    /// <summary>No units are present.</summary>
    None,
}
