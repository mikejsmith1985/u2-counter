namespace Counter.Infrastructure.Ai;

using System.Text.Json;
using Anthropic.Models.Messages;
using Counter.Infrastructure.Erp;

/// <summary>
/// The only things the assistant can do.
/// </summary>
/// <remarks>
/// This list is the read-only guarantee, expressed where it cannot be argued
/// with. There is no write tool, no delete tool and no arbitrary query tool, so a
/// model that decided to change something has nothing to decide it with — the
/// guarantee does not rest on the model behaving, on a prompt asking it to, or on
/// a check somewhere downstream.
///
/// The file a record may be read from is constrained too. Left open, "read record
/// from file X" is an arbitrary read of anything the database account can see,
/// which is most of the way back to the query tool this deliberately does not
/// offer.
/// </remarks>
public static class AskTools
{
    /// <summary>Find parts by number, description or manufacturer.</summary>
    public const string SearchParts = "search_parts";

    /// <summary>Read a part's live position at every branch.</summary>
    public const string ReadAvailability = "read_availability";

    /// <summary>Read a record exactly as the database stores it.</summary>
    public const string ReadRecord = "read_record";

    /// <summary>
    /// The files a record may be read from.
    /// </summary>
    /// <remarks>
    /// Named rather than left open. These are the four the screens already show,
    /// so the assistant can reach nothing a person using the application could
    /// not reach for themselves.
    /// </remarks>
    public static readonly IReadOnlySet<string> ReadableFiles = new HashSet<string>(
        StringComparer.Ordinal)
    {
        ErpFiles.Product.Name,
        ErpFiles.Inventory.Name,
        ErpFiles.Branch.Name,
        ErpFiles.Order.Name,
    };

    /// <summary>The tools as the model is given them.</summary>
    public static readonly IReadOnlyList<Tool> Definitions =
    [
        new Tool
        {
            Name = SearchParts,
            Description =
                "Find parts by number, description or manufacturer. Use this first " +
                "when the question names a part in words rather than by number. " +
                "Returns part number, description and manufacturer.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "query": {
                      "type": "string",
                      "description": "What to search for: a part number, a description, or a manufacturer."
                    }
                  },
                  "required": ["query"],
                  "additionalProperties": false
                }
                """),
        },
        new Tool
        {
            Name = ReadAvailability,
            Description =
                "Read one part's position at every branch: on hand, committed, and " +
                "free to sell. Free to sell is on hand minus committed and is the " +
                "number that answers whether stock can be promised. Read live on " +
                "every call; never cached.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "partNumber": {
                      "type": "string",
                      "description": "The exact part number, as returned by search_parts."
                    }
                  },
                  "required": ["partNumber"],
                  "additionalProperties": false
                }
                """),
        },
        new Tool
        {
            Name = ReadRecord,
            Description =
                "Read one record exactly as the MultiValue database stores it, with " +
                "its attribute, value and subvalue marks intact. Use this only when " +
                "somebody asks what the stored record looks like.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "file": {
                      "type": "string",
                      "enum": ["PRODUCT", "INVENTORY", "BRANCH", "ORDER"],
                      "description": "Which MultiValue file to read from."
                    },
                    "recordId": {
                      "type": "string",
                      "description": "The record key, for example a part number."
                    }
                  },
                  "required": ["file", "recordId"],
                  "additionalProperties": false
                }
                """),
        },
    ];

    /// <summary>
    /// Parse a schema written as JSON.
    /// </summary>
    /// <param name="json">The schema.</param>
    /// <returns>The schema in the shape the SDK expects.</returns>
    /// <remarks>
    /// Written as JSON rather than assembled from objects because a schema is
    /// read far more often than it is edited, and the JSON is the form every
    /// reference to it is written in.
    /// </remarks>
    private static InputSchema Schema(string json) =>
        JsonSerializer.Deserialize<InputSchema>(json)
            ?? throw new InvalidOperationException("A tool schema could not be parsed.");
}
