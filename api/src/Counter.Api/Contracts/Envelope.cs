namespace Counter.Api.Contracts;

/// <summary>
/// What every response carries about its own trustworthiness.
/// </summary>
/// <remarks>
/// These fields exist because the failure this feature most needs to avoid is a
/// partial or stale answer that reads as a whole, current one. A representative
/// repeats what the screen says to a customer; the screen must therefore be able
/// to say how much it is claiming.
/// </remarks>
/// <param name="IsComplete">
/// False when a limit was applied, so the answer may be partial.
/// </param>
/// <param name="Warning">What to tell the user when the answer may be partial.</param>
/// <param name="IsDemonstrationData">
/// Always true in this deployment. Rendered wherever figures appear, and carried
/// into anything the user copies, so a pasted number cannot later be mistaken for
/// a live one.
/// </param>
/// <param name="RetrievedAt">When the figures were read.</param>
public record ResponseEnvelope(
    bool IsComplete,
    string? Warning,
    bool IsDemonstrationData,
    DateTimeOffset RetrievedAt)
{
    /// <summary>An answer that is whole.</summary>
    public static ResponseEnvelope Complete() =>
        new(IsComplete: true, Warning: null, IsDemonstrationData: true, DateTimeOffset.UtcNow);

    /// <summary>An answer that may be partial, with the reason.</summary>
    /// <param name="warning">What the user should know, in words.</param>
    public static ResponseEnvelope Partial(string warning) =>
        new(IsComplete: false, warning, IsDemonstrationData: true, DateTimeOffset.UtcNow);
}
