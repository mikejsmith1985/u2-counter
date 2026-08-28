namespace Counter.Infrastructure.MultiValue;

/// <summary>
/// A record as the ERP stores it, split into fields, values and subvalues.
/// </summary>
/// <remarks>
/// Fields are one-based, matching how MultiValue documentation and every person
/// who works with these systems refers to them. A zero-based accessor here would
/// mean every call site translating, and one of them eventually forgetting.
/// </remarks>
public sealed class MultiValueRecord
{
    /// <summary>Separates fields within a record.</summary>
    public const char AttributeMark = (char)254;

    /// <summary>Separates values within a field.</summary>
    public const char ValueMark = (char)253;

    /// <summary>Separates subvalues within a value.</summary>
    public const char SubvalueMark = (char)252;

    private readonly string[] _fields;

    private MultiValueRecord(string raw, string[] fields)
    {
        Raw = raw;
        _fields = fields;
    }

    /// <summary>The record exactly as stored, separators included.</summary>
    public string Raw { get; }

    /// <summary>How many fields the record holds.</summary>
    public int FieldCount => _fields.Length;

    /// <summary>
    /// Parse a stored record.
    /// </summary>
    /// <param name="raw">The record as the ERP holds it.</param>
    public static MultiValueRecord Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        return new MultiValueRecord(
            raw,
            raw.Length == 0 ? [] : raw.Split(AttributeMark));
    }

    /// <summary>
    /// Read one field as a single string.
    /// </summary>
    /// <param name="fieldNumber">One-based field position.</param>
    /// <returns>The field, or empty when the record does not go that far.</returns>
    /// <remarks>
    /// A field beyond the end is empty rather than an error: MultiValue records
    /// do not store trailing empty fields, so a short record is normal data
    /// rather than a fault.
    /// </remarks>
    public string Field(int fieldNumber)
    {
        int index = fieldNumber - 1;
        return index >= 0 && index < _fields.Length ? _fields[index] : string.Empty;
    }

    /// <summary>
    /// Read one field as its list of values.
    /// </summary>
    /// <param name="fieldNumber">One-based field position.</param>
    /// <returns>Each value in order; empty when the field is absent or blank.</returns>
    public IReadOnlyList<string> Values(int fieldNumber)
    {
        string field = Field(fieldNumber);
        return field.Length == 0 ? [] : field.Split(ValueMark);
    }

    /// <summary>
    /// Read the subvalues of one value.
    /// </summary>
    /// <param name="fieldNumber">One-based field position.</param>
    /// <param name="valueNumber">One-based value position within the field.</param>
    public IReadOnlyList<string> Subvalues(int fieldNumber, int valueNumber)
    {
        IReadOnlyList<string> values = Values(fieldNumber);
        int index = valueNumber - 1;

        if (index < 0 || index >= values.Count)
        {
            return [];
        }

        return values[index].Split(SubvalueMark);
    }

    /// <summary>
    /// Read every field as a list of values, for showing the parsed form beside
    /// the raw record.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AsFieldMap()
    {
        Dictionary<string, IReadOnlyList<string>> map = [];

        for (int fieldNumber = 1; fieldNumber <= _fields.Length; fieldNumber++)
        {
            map[fieldNumber.ToString()] = Values(fieldNumber);
        }

        return map;
    }
}
