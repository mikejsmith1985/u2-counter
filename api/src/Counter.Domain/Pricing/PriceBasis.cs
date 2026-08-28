namespace Counter.Domain.Pricing;

/// <summary>What produced the price being quoted.</summary>
public enum PriceBasis
{
    /// <summary>The undiscounted catalogue price, because no terms apply.</summary>
    List,

    /// <summary>A customer's agreed price, produced by contract terms.</summary>
    Contract,
}
