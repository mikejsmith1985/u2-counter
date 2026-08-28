namespace Counter.Infrastructure.Mcp;

/// <summary>
/// The MCP tools this application may call, and those it may not.
/// </summary>
/// <remarks>
/// Both lists are here rather than in a document because a rule that only a
/// document states is a comment. <c>ForbiddenToolTests</c> asserts that no code
/// path binds to anything in <see cref="Forbidden"/>, so adding one fails a build.
///
/// The forbidden list is broader than "things that write". <c>execute_tcl</c>
/// reads perfectly well, and it is excluded anyway: no question in this feature
/// needs a system-level command, and never calling it means we do not depend on
/// the server's blocklist being complete.
/// </remarks>
public static class ErpTools
{
    /// <summary>Read one record from a file by its key.</summary>
    public const string ReadRecord = "read_record";

    /// <summary>Read several records from one file.</summary>
    public const string ReadRecords = "read_records";

    /// <summary>Run a read-only query.</summary>
    public const string ExecuteQuery = "execute_query";

    /// <summary>List a file's dictionary items.</summary>
    public const string ListDictionary = "list_dictionary";

    /// <summary>Run a selection and return the matching keys.</summary>
    /// <remarks>
    /// Keys come back as structured data. A LIST returns the records formatted
    /// for a person to read, with the separators converted to newlines — which
    /// is right for display and useless for parsing, because the structure the
    /// separators carried is gone.
    /// </remarks>
    public const string GetSelectList = "get_select_list";

    /// <summary>Every tool this application is permitted to call.</summary>
    public static readonly IReadOnlySet<string> Permitted = new HashSet<string>
    {
        ReadRecord,
        ReadRecords,
        ExecuteQuery,
        ListDictionary,
        GetSelectList,
    };

    /// <summary>
    /// Tools this application must never call, each for a stated reason.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Forbidden =
        new Dictionary<string, string>
        {
            ["write_record"] = "The feature is read-only against the ERP.",
            ["delete_record"] = "The feature is read-only against the ERP.",
            ["execute_tcl"] =
                "No question in this feature needs a system-level command, and never " +
                "calling it means we do not depend on the server's blocklist being complete.",
            ["call_subroutine"] = "Runs business logic this feature does not use.",
            ["begin_transaction"] = "Nothing is written, so nothing needs a transaction.",
            ["commit_transaction"] = "Nothing is written, so nothing needs a transaction.",
            ["rollback_transaction"] = "Nothing is written, so nothing needs a transaction.",
            ["save_knowledge"] = "Writes to the server's own store; not this feature's concern.",
            ["delete_knowledge"] = "Writes to the server's own store; not this feature's concern.",
        };
}
