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

    /// <summary>Name every field in a file, from the file's own dictionary.</summary>
    /// <remarks>
    /// A MultiValue database describes itself. The dictionary says which position
    /// holds what, how to display it, and whether it is multi-valued — so a
    /// screen built from it fits whatever account it is pointed at, instead of
    /// fitting only the one layout somebody compiled into it.
    /// </remarks>
    public const string ListDictionary = "list_dictionary";

    /// <summary>Name the files in the account.</summary>
    public const string ListFiles = "list_files";

    /// <summary>Run a selection and return the matching keys.</summary>
    /// <remarks>
    /// Keys come back as structured data. A LIST returns the records formatted
    /// for a person to read, with the separators converted to newlines — which
    /// is right for display and useless for parsing, because the structure the
    /// separators carried is gone.
    /// </remarks>
    public const string GetSelectList = "get_select_list";

    /// <summary>Every tool this application is permitted to call.</summary>
    /// <remarks>
    /// Each name here is called by <c>ErpReader</c>, and a test fails the build if
    /// one stops being. A permission nobody uses is one nobody will question when
    /// it later turns out to matter, so the list is kept to what is actually
    /// needed rather than to what might be.
    ///
    /// <c>list_dictionary</c> and <c>list_files</c> were both excluded here, and
    /// the reason has since stopped being true. The stated reason was that the
    /// demonstration store held no dictionaries, so there was nothing for the
    /// first to read -- and that was correct until the store gained real ones.
    ///
    /// They are permitted now because the application asks a question it could
    /// not ask before: what is in this account, and what do its fields mean? A
    /// screen built from a file's own dictionary fits whatever account it is
    /// pointed at. Both are reads, and neither can name anything the account
    /// does not already contain.
    ///
    /// <c>execute_query</c> ran arbitrary query text. Nothing called it: every
    /// question this application asks is a keyed read or a parameterised
    /// selection. It sat here permitted for weeks because the test meant to catch
    /// exactly this could not -- it searched the reader's source for the constant
    /// name, and the constant was present in a method nobody invoked. Of all the
    /// permissions to leave dangling, the arbitrary-query one is the worst.
    /// </remarks>
    public static readonly IReadOnlySet<string> Permitted = new HashSet<string>
    {
        ReadRecord,
        ReadRecords,
        GetSelectList,
        ListDictionary,
        ListFiles,
    };

    /// <summary>
    /// Tools this application must never call, each for a stated reason.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Forbidden =
        new Dictionary<string, string>
        {
            ["write_record"] = "The feature is read-only against the ERP.",
            ["delete_record"] = "The feature is read-only against the ERP.",
            ["execute_query"] =
                "Runs arbitrary query text. Every question this application asks is a " +
                "keyed read or a parameterised selection, so the ability to send a " +
                "query somebody composed is capability it has no use for.",
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
