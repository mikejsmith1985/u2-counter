namespace Counter.Infrastructure.Mcp;

/// <summary>
/// Reads the ERP. Nothing here writes, and nothing here can be made to.
/// </summary>
public interface IErpReader
{
    /// <summary>
    /// Read one record in its stored form, separators included.
    /// </summary>
    /// <param name="fileName">The MultiValue file.</param>
    /// <param name="recordId">The record's key.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <exception cref="ErpRecordNotFoundException">If the key does not exist.</exception>
    /// <exception cref="ErpUnreachableException">If the ERP does not answer in time.</exception>
    Task<string> ReadRecordAsync(string fileName, string recordId, CancellationToken cancellationToken);

    /// <summary>
    /// Run a read-only query.
    /// </summary>
    /// <param name="query">The query, built from validated parameters only.</param>
    /// <param name="maxRows">Most rows to return.</param>
    /// <param name="cancellationToken">Abandons the query when the caller gives up.</param>
    /// <exception cref="ErpRefusedException">If the ERP refused the query.</exception>
    /// <exception cref="ErpUnreachableException">If the ERP does not answer in time.</exception>
    Task<ErpQueryResult> QueryAsync(string query, int maxRows, CancellationToken cancellationToken);

    /// <summary>
    /// Run a selection and return the matching keys.
    /// </summary>
    /// <param name="query">A SELECT or SSELECT statement.</param>
    /// <param name="maxKeys">Most keys to return.</param>
    /// <param name="cancellationToken">Abandons the selection when the caller gives up.</param>
    /// <remarks>
    /// Keys rather than formatted output, because a LIST returns records rendered
    /// for a person to read: the separators become newlines, and the structure
    /// they carried is gone.
    /// </remarks>
    Task<IReadOnlyList<string>> SelectKeysAsync(
        string query, int maxKeys, CancellationToken cancellationToken);

    /// <summary>
    /// Read several records at once, in their stored form.
    /// </summary>
    /// <param name="fileName">The MultiValue file.</param>
    /// <param name="recordIds">The keys to read.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <returns>Each record found, keyed by its id. Missing keys are simply absent.</returns>
    Task<IReadOnlyDictionary<string, string>> ReadRecordsAsync(
        string fileName, IReadOnlyList<string> recordIds, CancellationToken cancellationToken);
}

/// <summary>
/// What a query returned, and whether it is the whole answer.
/// </summary>
/// <param name="Output">The server's output.</param>
/// <param name="IsComplete">
/// False when a limit was applied. Carried all the way to the screen: an answer
/// that may be partial must never be presented as whole.
/// </param>
/// <param name="Warning">What to tell the user when the answer may be partial.</param>
public sealed record ErpQueryResult(string Output, bool IsComplete, string? Warning);

/// <summary>Where the MCP server is, and how long to wait for it.</summary>
public sealed class ErpConnectionOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Erp";

    /// <summary>
    /// The MCP server's endpoint. Loopback by default, because the server refuses
    /// to serve unauthenticated traffic on a reachable interface.
    /// </summary>
    public Uri Endpoint { get; set; } = new("http://127.0.0.1:5081/");

    /// <summary>
    /// How long to wait before abandoning a request.
    /// </summary>
    /// <remarks>
    /// Five seconds is the outer edge of what someone on a phone call will
    /// tolerate, and comfortably outside the one-second target for a healthy
    /// answer. Waiting longer does not help them: they can excuse themselves and
    /// ring back, but they cannot repeat a figure they never received.
    /// </remarks>
    public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Raised when the ERP does not answer within the budget.</summary>
/// <param name="message">What to tell the user.</param>
public sealed class ErpUnreachableException(string message) : Exception(message);

/// <summary>Raised when the ERP refuses a request.</summary>
/// <param name="message">What the ERP said.</param>
public sealed class ErpRefusedException(string message) : Exception(message);

/// <summary>Raised when a record does not exist.</summary>
/// <param name="message">Which key was missing, and where.</param>
public sealed class ErpRecordNotFoundException(string message) : Exception(message);
