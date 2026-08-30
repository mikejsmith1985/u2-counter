namespace Counter.Infrastructure.Ai;

using System.Text.Json;
using Anthropic.Models.Messages;
using Counter.Infrastructure.Erp;

/// <summary>
/// The only things the assistant can do.
/// </summary>
/// <remarks>
/// This list is the read-only guarantee, expressed where it cannot be argued
/// with. There is no write tool and no delete tool, so a model that decided to
/// change something has nothing to decide it with — the guarantee does not rest
/// on the model behaving, on a prompt asking it to, or on a check somewhere
/// downstream.
///
/// There is a query tool, and this comment used to say there deliberately was
/// not. That changed when the honest answer to "does this work against a schema
/// nobody anticipated?" turned out to be no: an assistant holding only tools
/// compiled to one layout is useful exactly as far as somebody's foresight went.
/// The account can now be asked what it holds, and any of it selected from.
///
/// What did not change is that every one of these is a read. The selection tool
/// refuses any verb that is not SELECT or SSELECT, here and again at the MCP
/// server, which allows read verbs only. Two places, both deliberate, and neither
/// of them the model.
///
/// `read_record` stays constrained to four files even so. Not for safety, since
/// the query tool reaches the same records, but because it returns the stored
/// bytes with their marks intact rather than a rendering of them, and that is the
/// only thing that shows what the data actually is.
/// </remarks>
public static class AskTools
{
    /// <summary>Find parts by number, description or manufacturer.</summary>
    public const string SearchParts = "search_parts";

    /// <summary>Read a part's live position at every branch.</summary>
    public const string ReadAvailability = "read_availability";

    /// <summary>Read a record exactly as the database stores it.</summary>
    public const string ReadRecord = "read_record";

    /// <summary>What one named customer pays for a part.</summary>
    public const string ReadPrice = "read_price";

    /// <summary>What every price class pays for a part, cheapest first.</summary>
    public const string ComparePrices = "compare_prices";

    /// <summary>Every file in the account, whatever the schema is.</summary>
    public const string ListFiles = "list_files";

    /// <summary>One file's dictionary: what its fields are called and where they live.</summary>
    public const string DescribeFile = "describe_file";

    /// <summary>A read-only selection against any file.</summary>
    public const string Query = "query";

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
        new Tool
        {
            Name = ReadPrice,
            Description =
                "What one customer pays for one part: list price, the multiplier " +
                "their contract applies, and the net. Use this when a question names " +
                "a customer, or when a customer is already selected on screen.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "partNumber": {
                      "type": "string",
                      "description": "The exact part number, as returned by search_parts."
                    },
                    "customerAccount": {
                      "type": "string",
                      "description": "The customer's account number."
                    }
                  },
                  "required": ["partNumber", "customerAccount"],
                  "additionalProperties": false
                }
                """),
        },
        new Tool
        {
            Name = ComparePrices,
            Description =
                "What a part costs across every price class, cheapest first, with " +
                "the customers in each. Use this for \"who gets the best price on " +
                "this\" and any question comparing customers. One call covers them " +
                "all -- a price belongs to a price class, and customers are assigned " +
                "to one, so there is no need to ask about accounts one at a time.",
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
            Name = ListFiles,
            Description =
                "Name every file in this account. Use it when a question is about " +
                "data the other tools do not cover, or when you do not yet know what " +
                "this database holds. Works against any schema, because the account " +
                "is asked rather than assumed.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {},
                  "additionalProperties": false
                }
                """),
        },
        new Tool
        {
            Name = DescribeFile,
            Description =
                "Read one file's dictionary: every field's name, its position, its " +
                "heading, and whether it holds one value or many. A MultiValue " +
                "database describes itself, so this is how to learn an unfamiliar " +
                "file rather than guessing at field names. Do this before querying " +
                "a file you have not met.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "file": {
                      "type": "string",
                      "description": "The file name, as returned by list_files."
                    }
                  },
                  "required": ["file"],
                  "additionalProperties": false
                }
                """),
        },
        new Tool
        {
            Name = Query,
            Description =
                "Run a read-only selection and read back what it found. The statement " +
                "must begin with SELECT or SSELECT -- anything else is refused, and " +
                "there is no way to write through this tool. Use the field names from " +
                "describe_file. Example: SELECT PRODUCT WITH CATEGORY = \"CON\".",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "statement": {
                      "type": "string",
                      "description": "A SELECT or SSELECT statement, using field names from the file's dictionary."
                    },
                    "file": {
                      "type": "string",
                      "description": "The file the statement selects from, so the records found can be read back."
                    }
                  },
                  "required": ["statement", "file"],
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
