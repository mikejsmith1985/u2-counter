namespace Counter.Domain.Pricing;

/// <summary>
/// An agreement that turns a list price into what one customer pays.
/// </summary>
/// <param name="Multiplier">Fraction of list price. 0.70 means thirty per cent off.</param>
/// <param name="EffectiveFrom">First day the terms apply.</param>
/// <param name="EffectiveTo">Last day the terms apply.</param>
/// <param name="Description">How the terms read to a person, for explaining a price aloud.</param>
public sealed record ContractTerms(
    decimal Multiplier,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    string Description)
{
    /// <summary>
    /// Whether these terms are in force on a given day.
    /// </summary>
    /// <param name="on">The day to test, normally today.</param>
    public bool AppliesOn(DateOnly on) => on >= EffectiveFrom && on <= EffectiveTo;
}
