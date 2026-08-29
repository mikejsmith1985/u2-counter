namespace Counter.Infrastructure.MultiValue;

/// <summary>
/// The separators a stored record holds, described so they can be shown.
/// </summary>
/// <remarks>
/// The marks are invisible on a screen. Rendering a record raw would show a run of
/// values with nothing between them, which teaches a reader less than nothing: it
/// suggests the structure is not there.
///
/// So each mark present gets a name and a plain statement of what it separates,
/// and the client draws a badge. What is described is only what the record
/// actually contains — labelling a subvalue mark on a record with none would
/// explain a structure that is not there, which is the same mistake in reverse.
/// </remarks>
public static class RecordMarks
{
    /// <summary>Every mark this format uses, in nesting order.</summary>
    private static readonly RecordMark[] All =
    [
        new(MultiValueRecord.AttributeMark, "Attribute mark", "Fields"),
        new(MultiValueRecord.ValueMark, "Value mark", "Values within a field"),
        new(MultiValueRecord.SubvalueMark, "Subvalue mark", "Sub-items within a value"),
    ];

    /// <summary>
    /// Describe the marks a record contains.
    /// </summary>
    /// <param name="raw">The record exactly as stored.</param>
    /// <returns>One description per mark present, outermost first.</returns>
    public static IReadOnlyList<RecordMark> PresentIn(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        return All
            .Where(mark => raw.Contains(mark.Character, StringComparison.Ordinal))
            .ToList();
    }
}

/// <summary>
/// One separator, named.
/// </summary>
/// <param name="Character">The character itself.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Separates">What it separates, in words a non-developer can read.</param>
public sealed record RecordMark(char Character, string Name, string Separates)
{
    /// <summary>Its code point, which is how these marks are usually referred to.</summary>
    public int Code => Character;
}
