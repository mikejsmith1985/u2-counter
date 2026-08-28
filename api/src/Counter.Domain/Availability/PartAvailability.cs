namespace Counter.Domain.Availability;

using Counter.Domain.Catalogue;

/// <summary>
/// Everything known about where one part can be had.
/// </summary>
/// <param name="Part">The catalogue item.</param>
/// <param name="Positions">One entry per branch holding a position for it.</param>
/// <param name="IsStockKnown">
/// False when the part exists but has no inventory record at all. That is a
/// different answer from zero stock, and conflating them would have a
/// representative tell a customer there is none when nobody has counted.
/// </param>
public sealed record PartAvailability(
    Part Part,
    IReadOnlyList<BranchPosition> Positions,
    bool IsStockKnown)
{
    /// <summary>Units free to sell across every branch.</summary>
    public int TotalFreeToSell => Positions.Sum(position => position.FreeToSell);

    /// <summary>Units physically present across every branch.</summary>
    public int TotalOnHand => Positions.Sum(position => position.OnHand);

    /// <summary>
    /// The branches with stock free to sell, most first, for suggesting where to source from.
    /// </summary>
    public IReadOnlyList<BranchPosition> BranchesWithStock =>
        Positions.Where(position => position.FreeToSell > 0)
                 .OrderByDescending(position => position.FreeToSell)
                 .ToList();
}
