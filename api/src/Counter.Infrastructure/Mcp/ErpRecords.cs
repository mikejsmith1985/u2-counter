namespace Counter.Infrastructure.Mcp;

using System.Text;
using System.Text.Json;
using Counter.Infrastructure.MultiValue;

/// <summary>
/// Rebuilds stored records from the structured form the MCP server returns.
/// </summary>
/// <remarks>
/// The server offers two ways to read data, and only one of them is usable here.
/// A <c>LIST</c> returns records formatted for a person: the separators are
/// converted to newlines and pipes, which is exactly right on a screen and
/// useless for parsing, because the structure those separators carried has gone.
///
/// The record tools return fields as structured JSON instead, which survives the
/// journey. This class puts the separators back so the rest of the application —
/// and the record view, which shows the stored form to a user — works with what
/// the ERP actually holds.
/// </remarks>
public static class ErpRecords
{
    /// <summary>
    /// Rebuild one record from its fields.
    /// </summary>
    /// <param name="fields">Fields keyed by their one-based position.</param>
    /// <returns>The record in its stored form, separators included.</returns>
    public static string Rebuild(JsonElement fields)
    {
        if (fields.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        SortedDictionary<int, string> byPosition = [];

        foreach (JsonProperty field in fields.EnumerateObject())
        {
            if (int.TryParse(field.Name, out int position))
            {
                byPosition[position] = JoinValues(field.Value);
            }
        }

        if (byPosition.Count == 0)
        {
            return string.Empty;
        }

        // Fields are rejoined by position, filling any gap with an empty field.
        // Skipping a gap would shift every field after it, which is the same
        // silent corruption the parser guards against at the other end.
        StringBuilder record = new();

        for (int position = 1; position <= byPosition.Keys.Max(); position++)
        {
            if (position > 1)
            {
                record.Append(MultiValueRecord.AttributeMark);
            }

            record.Append(byPosition.GetValueOrDefault(position, string.Empty));
        }

        return record.ToString();
    }

    /// <summary>Read the record keys from a select-list response.</summary>
    public static IReadOnlyList<string> RecordIdsFrom(JsonElement payload)
    {
        if (!payload.TryGetProperty("record_ids", out JsonElement ids) ||
            ids.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return ids.EnumerateArray()
            .Select(id => id.GetString() ?? string.Empty)
            .Where(id => id.Length > 0)
            .ToList();
    }

    /// <summary>Read each record from a batch response, keyed by its id.</summary>
    public static IReadOnlyDictionary<string, string> RecordsFrom(JsonElement payload)
    {
        Dictionary<string, string> records = [];

        if (!payload.TryGetProperty("records", out JsonElement list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return records;
        }

        foreach (JsonElement entry in list.EnumerateArray())
        {
            if (entry.TryGetProperty("id", out JsonElement id) &&
                entry.TryGetProperty("fields", out JsonElement fields))
            {
                records[id.GetString() ?? string.Empty] = Rebuild(fields);
            }
        }

        return records;
    }

    /// <summary>Join a field's values, preserving subvalues within them.</summary>
    private static string JoinValues(JsonElement field)
    {
        if (field.ValueKind != JsonValueKind.Array)
        {
            return field.ToString();
        }

        return string.Join(
            MultiValueRecord.ValueMark,
            field.EnumerateArray().Select(JoinSubvalues));
    }

    /// <summary>Join one value's subvalues.</summary>
    private static string JoinSubvalues(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return value.ToString();
        }

        return string.Join(
            MultiValueRecord.SubvalueMark,
            value.EnumerateArray().Select(item => item.ToString()));
    }
}
