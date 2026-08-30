namespace Counter.Infrastructure.Ai;

/// <summary>
/// What the assistant asked the database, and what came back.
/// </summary>
/// <remarks>
/// The transcript is the point of the feature, not a debugging aid.
///
/// A reviewer looking at a screen that says "299 free to sell at Grand Junction"
/// has no way to know where that came from. It could be a mocked record, or a row
/// from a relational table dressed up with separators. The only thing that
/// settles it is showing the call that was made and the bytes that came back —
/// which is what this carries.
///
/// It also shows the read-only guarantee doing work rather than being claimed:
/// every call listed here is a read, and there is no write in the tool list for
/// the model to reach for.
/// </remarks>
/// <param name="Tool">Which tool was called, by the name the model saw.</param>
/// <param name="Arguments">What it was called with.</param>
/// <param name="File">The MultiValue file the call reached, when it reached one.</param>
/// <param name="RecordId">The key it read, when it read one.</param>
/// <param name="RawRecord">
/// The record exactly as the database returned it, separators included. Empty for
/// calls that read no single record.
/// </param>
/// <param name="Summary">One line a person can read, describing the call.</param>
/// <param name="Result">
/// What the tool actually handed back to the model.
///
/// Carried because a summary is a claim and this is the evidence for it. A step
/// reading "8 parts matched" tells a reader that a number was produced and
/// nothing about which parts, so somebody checking whether the answer follows
/// from the data has been given the answer twice and the data never.
/// </param>
/// <param name="DurationMs">How long the call took.</param>
public sealed record AskStep(
    string Tool,
    string Arguments,
    string File,
    string RecordId,
    string RawRecord,
    string Summary,
    string Result,
    int DurationMs);

/// <summary>
/// One answer, with the working shown.
/// </summary>
/// <param name="Answer">What the assistant said.</param>
/// <param name="Steps">Every call it made to get there, in order.</param>
/// <param name="Model">Which model answered. Reported so nobody has to trust that it was the cheap one.</param>
/// <param name="InputTokens">Tokens read.</param>
/// <param name="OutputTokens">Tokens written.</param>
/// <param name="QuestionsLeft">How many more this session may ask.</param>
/// <param name="Looking">
/// What was on the person's screen when they asked, as a short line.
///
/// Reported because an answer that resolves "these" to a part nobody typed looks
/// like the model remembering the data, which is the one thing this must never
/// look like. Saying what it was told keeps the difference visible: it was given
/// the part number, and every figure still came from a call listed above.
/// </param>
public sealed record AskResult(
    string Answer,
    IReadOnlyList<AskStep> Steps,
    string Model,
    int InputTokens,
    int OutputTokens,
    int QuestionsLeft,
    string Looking);

/// <summary>
/// Why the assistant would not answer.
/// </summary>
/// <remarks>
/// Separate from a failure. Every reason here is the system working: a limit was
/// reached, or no key was configured. Told apart because they need different
/// words on screen — "come back tomorrow" and "this was never switched on" are
/// not the same message.
/// </remarks>
public enum AskRefusal
{
    /// <summary>No refusal; the question was answered.</summary>
    None = 0,

    /// <summary>No key is configured, so there is no assistant.</summary>
    NotConfigured,

    /// <summary>This session has asked its allowance of questions.</summary>
    SessionLimitReached,

    /// <summary>The deployment has spent its allowance for today.</summary>
    DailyLimitReached,

    /// <summary>The question was empty.</summary>
    NothingAsked,
}
