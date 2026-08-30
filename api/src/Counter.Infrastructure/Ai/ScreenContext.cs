namespace Counter.Infrastructure.Ai;

/// <summary>
/// What the person asking is currently looking at.
/// </summary>
/// <remarks>
/// People at a counter do not repeat themselves. With a part on screen they ask
/// "how quickly can we get twenty of these to Denver?", and an assistant that
/// answers "which part?" when the part number is six inches away has failed at
/// the thing it was added for.
///
/// The distinction this type is built around: context supplies the <em>referent</em>,
/// never the facts. Being told which part is on screen lets the model resolve
/// "these"; it does not tell it a single quantity, price or branch. Those still
/// come back from a tool call or they do not get said, which is the rule the
/// whole feature rests on and the one it would be easiest to quietly break here.
/// </remarks>
/// <param name="PartNumber">The part on screen, if there is one.</param>
/// <param name="PartDescription">Its description, for a question phrased in words rather than numbers.</param>
/// <param name="CustomerAccount">The customer being served, if one is selected.</param>
/// <param name="CustomerName">That customer's name, for the same reason.</param>
public sealed record ScreenContext(
    string? PartNumber,
    string? PartDescription,
    string? CustomerAccount,
    string? CustomerName)
{
    /// <summary>Nothing selected; the assistant is on its own.</summary>
    public static readonly ScreenContext Empty = new(null, null, null, null);

    /// <summary>Whether anything is worth telling the model about.</summary>
    public bool HasAnything =>
        !string.IsNullOrWhiteSpace(PartNumber) || !string.IsNullOrWhiteSpace(CustomerAccount);

    /// <summary>
    /// The context as a line for the model, or empty when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Written to close off the shortcut it would otherwise open. Naming the part
    /// on screen invites the model to answer from the name — so the last sentence
    /// says plainly that no figures came with it. Without that, an assistant told
    /// "the person is looking at part X" will cheerfully produce a stock figure
    /// for part X that no tool ever returned.
    /// </remarks>
    public string ForModel()
    {
        if (!HasAnything)
        {
            return string.Empty;
        }

        List<string> onScreen = [];

        if (!string.IsNullOrWhiteSpace(PartNumber))
        {
            string described = string.IsNullOrWhiteSpace(PartDescription)
                ? PartNumber
                : $"{PartNumber} ({PartDescription})";

            onScreen.Add($"the part {described}");
        }

        if (!string.IsNullOrWhiteSpace(CustomerAccount))
        {
            string described = string.IsNullOrWhiteSpace(CustomerName)
                ? CustomerAccount
                : $"{CustomerName}, account {CustomerAccount}";

            onScreen.Add($"the customer {described}");
        }

        return "\n\nThe person is looking at " + string.Join(" and ", onScreen) + ". "
            + "Read \"this\", \"these\", \"it\" and \"them\" as that part, and an "
            + "unnamed customer as that customer. Do not ask which part they mean "
            + "when it is named here.\n\n"
            + "No figures came with this. You have been told what is on screen and "
            + "nothing about its stock, price or branches — those still come from a "
            + "tool call or they do not get said.";
    }

    /// <summary>
    /// One line a person can read, for the transcript.
    /// </summary>
    /// <remarks>
    /// Shown so a reader can see why the assistant knew what "these" meant. An
    /// answer that resolves a pronoun out of nowhere looks like the model
    /// remembering the data, which is the one thing this must never look like.
    /// </remarks>
    public string ForReader()
    {
        if (!HasAnything)
        {
            return string.Empty;
        }

        List<string> parts = [];

        if (!string.IsNullOrWhiteSpace(PartNumber))
        {
            parts.Add(PartNumber);
        }

        if (!string.IsNullOrWhiteSpace(CustomerAccount))
        {
            parts.Add(CustomerAccount);
        }

        return string.Join(" · ", parts);
    }
}
