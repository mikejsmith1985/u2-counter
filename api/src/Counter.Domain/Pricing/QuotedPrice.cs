namespace Counter.Domain.Pricing;

/// <summary>
/// What a customer pays for a part, and how that number was arrived at.
/// </summary>
/// <remarks>
/// The workings travel with the answer deliberately. A representative reading a
/// price aloud is often asked "why that much?", and a screen showing only the net
/// figure forces them to escalate a question they could have answered.
/// </remarks>
/// <param name="ListPrice">The undiscounted price.</param>
/// <param name="NetPrice">What this customer pays.</param>
/// <param name="Basis">Whether contract terms applied.</param>
/// <param name="AppliedTerms">The terms that produced the net price, if any.</param>
/// <param name="DisregardedTerms">
/// Terms that exist for this customer and part but were not applied, each with the
/// reason. Usually an expired promotion, which is exactly the case where a price
/// looks unexpectedly high and someone needs to explain it on the call.
/// </param>
public sealed record QuotedPrice(
    decimal ListPrice,
    decimal NetPrice,
    PriceBasis Basis,
    ContractTerms? AppliedTerms,
    IReadOnlyList<DisregardedTerms> DisregardedTerms);

/// <summary>
/// Terms that were available but did not apply, and why not.
/// </summary>
/// <param name="Terms">The terms in question.</param>
/// <param name="Reason">Why they were not applied, in words a person can repeat.</param>
public sealed record DisregardedTerms(ContractTerms Terms, string Reason);
