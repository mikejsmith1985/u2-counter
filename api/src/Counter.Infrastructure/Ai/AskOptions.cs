namespace Counter.Infrastructure.Ai;

/// <summary>
/// Every limit on what the assistant may spend, in one place.
/// </summary>
/// <remarks>
/// This exists because the demonstration is public and the key behind it is a
/// personal one. Somebody idly holding down Enter must not be able to run up a
/// bill, and the person who owns the key is not watching.
///
/// The model is not configurable, and that is deliberate rather than an
/// oversight. A model name that can be set is a model name that can be set to an
/// expensive one — by a configuration mistake, or by anything that can reach
/// configuration. Haiku is twenty-five times cheaper on output than the Opus tier
/// and is more than capable of "which branch has this part", which is the entire
/// job here.
/// </remarks>
public sealed class AskOptions
{
    /// <summary>
    /// The only model this application will ever call.
    /// </summary>
    /// <remarks>
    /// A constant, not a setting. There is no code path that reads a model name
    /// from configuration or from a request, so there is no code path that can
    /// be pointed at a more expensive one.
    /// </remarks>
    public const string Model = "claude-haiku-4-5";

    /// <summary>
    /// Most tokens the assistant may write in one answer.
    /// </summary>
    /// <remarks>
    /// A counter answer is two or three sentences. This is enough for that with
    /// room to spare, and far too little to be worth abusing.
    /// </remarks>
    public int MaxTokens { get; init; } = 700;

    /// <summary>
    /// How many rounds of tool calls the assistant may take before it must answer.
    /// </summary>
    /// <remarks>
    /// Four covers the longest honest journey: find the part, read its stock,
    /// read the customer's price, read the record. A loop that cannot end is the
    /// classic way an agent turns one question into a hundred requests.
    /// </remarks>
    public int MaxToolRounds { get; init; } = 4;

    /// <summary>
    /// How many tool calls the assistant may make in total, across all rounds.
    /// </summary>
    /// <remarks>
    /// Rounds alone are not a limit, and finding that out is the reason this
    /// exists. Claude asks for tools in parallel: a single round can carry as
    /// many calls as it likes, so four rounds bounded the conversation and
    /// bounded nothing about the work. The first real question -- "which branch
    /// has the most" -- produced eight reads inside the four-round ceiling, and
    /// a question naming a broader category would have produced far more.
    ///
    /// Twelve is generous for an honest question and small enough that a
    /// dishonest one costs pennies.
    /// </remarks>
    public int MaxToolCallsInTotal { get; init; } = 12;

    /// <summary>Most questions one browser session may ask.</summary>
    public int QuestionsPerSession { get; init; } = 12;

    /// <summary>
    /// Most tokens the whole deployment may spend in a day, across everyone.
    /// </summary>
    /// <remarks>
    /// The backstop that does not depend on anyone behaving. At Haiku's prices
    /// this ceiling is worth a little over a dollar a day, which is the most this
    /// demonstration can cost however many people find it.
    /// </remarks>
    public int TokensPerDay { get; init; } = 200_000;

    /// <summary>Whether a key is configured at all.</summary>
    /// <remarks>
    /// Absent is a supported state, not a broken one: the application runs
    /// without an assistant and says so, which is how it runs on a laptop with no
    /// key and how it behaved before this existed.
    /// </remarks>
    public bool IsConfigured { get; init; }
}
