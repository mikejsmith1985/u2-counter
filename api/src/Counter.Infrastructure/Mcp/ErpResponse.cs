namespace Counter.Infrastructure.Mcp;

using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// Reads what the MCP server returned, keeping the parts that must survive.
/// </summary>
/// <remarks>
/// The server reports whether an answer is complete, and whether something went
/// wrong. Both must reach the client untouched: an incomplete answer presented as
/// whole, or a failure presented as an empty result, is exactly the confusion this
/// feature forbids. This class therefore adds no interpretation of its own.
/// </remarks>
public static class ErpResponse
{
    /// <summary>
    /// Take the JSON payload out of a tool result.
    /// </summary>
    /// <param name="result">What the server returned.</param>
    /// <exception cref="ErpRefusedException">
    /// If the server refused the call, or returned something that is not JSON.
    /// </exception>
    public static JsonElement PayloadFrom(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string text = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .FirstOrDefault() ?? string.Empty;

        if (result.IsError == true)
        {
            throw new ErpRefusedException(
                text.Length > 0 ? text : "The ERP refused the request without explanation.");
        }

        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new ErpRefusedException(
                $"The ERP returned something this application could not read: {error.Message}");
        }
    }

    /// <summary>
    /// Read a raw record out of a <c>read_record</c> response.
    /// </summary>
    /// <exception cref="ErpRecordNotFoundException">If the record does not exist.</exception>
    public static string RawRecordFrom(JsonElement payload, string fileName, string recordId)
    {
        if (TryReadError(payload, out string? message))
        {
            throw new ErpRecordNotFoundException(
                $"'{recordId}' was not found in '{fileName}': {message}");
        }

        // The server returns the record's fields; the raw form is what the record
        // view needs, so it is preferred where present.
        if (payload.TryGetProperty("raw", out JsonElement raw) &&
            raw.ValueKind == JsonValueKind.String)
        {
            return raw.GetString() ?? string.Empty;
        }

        return RebuildFromFields(payload);
    }

    /// <summary>
    /// Read a query response, preserving whether the answer is complete.
    /// </summary>
    /// <exception cref="ErpRefusedException">If the server reported an error.</exception>
    public static ErpQueryResult QueryResultFrom(JsonElement payload, string query)
    {
        if (TryReadError(payload, out string? message))
        {
            throw new ErpRefusedException($"The ERP refused '{query}': {message}");
        }

        string output = payload.TryGetProperty("output", out JsonElement outputElement)
            ? outputElement.GetString() ?? string.Empty
            : string.Empty;

        // Absent means complete, but only because the server states completeness
        // explicitly when it is not. Defaulting the other way would report every
        // answer as partial.
        bool isComplete = !payload.TryGetProperty("is_complete", out JsonElement complete) ||
                          complete.ValueKind != JsonValueKind.False;

        string? warning = payload.TryGetProperty("warning", out JsonElement warningElement) &&
                          warningElement.ValueKind == JsonValueKind.String
            ? warningElement.GetString()
            : null;

        return new ErpQueryResult(output, isComplete, warning);
    }

    /// <summary>Whether the payload carries an error, and what it says.</summary>
    private static bool TryReadError(JsonElement payload, out string? message)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("error", out JsonElement error) &&
            error.ValueKind == JsonValueKind.String)
        {
            message = error.GetString();
            return true;
        }

        message = null;
        return false;
    }

    /// <summary>
    /// Reassemble a record from its parsed fields, when no raw form was returned.
    /// </summary>
    /// <remarks>
    /// Fields come back keyed by their one-based position, so they are rejoined in
    /// numeric order rather than the order the object happens to list them.
    /// </remarks>
    private static string RebuildFromFields(JsonElement payload)
    {
        if (!payload.TryGetProperty("fields", out JsonElement fields) ||
            fields.ValueKind != JsonValueKind.Object)
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

        return string.Join(MultiValue.MultiValueRecord.AttributeMark, byPosition.Values);
    }

    /// <summary>Join a field's values with value marks, preserving subvalues.</summary>
    private static string JoinValues(JsonElement field)
    {
        if (field.ValueKind != JsonValueKind.Array)
        {
            return field.GetString() ?? string.Empty;
        }

        return string.Join(
            MultiValue.MultiValueRecord.ValueMark,
            field.EnumerateArray().Select(JoinSubvalues));
    }

    /// <summary>Join one value's subvalues with subvalue marks.</summary>
    private static string JoinSubvalues(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Join(
            MultiValue.MultiValueRecord.SubvalueMark,
            value.EnumerateArray().Select(item => item.GetString() ?? string.Empty));
    }
}
