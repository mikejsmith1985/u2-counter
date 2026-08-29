namespace Counter.Infrastructure.Mcp;

using Counter.Infrastructure.MultiValue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// Writes a record through the MCP server, and reads it back to check.
/// </summary>
/// <remarks>
/// Registered only when a deployment configures a writable ERP driver. The
/// demonstration does not, so on the deployed application this class is never
/// constructed and the endpoint that needs it says so.
///
/// It opens its own connection rather than sharing the reader's, and that is
/// deliberate. The reader holds one long-lived session because search is
/// constant and a connection per request would leak one per burst; writes are
/// rare — a person reading a record and pressing confirm — so a connection per
/// write costs nothing and means a write can never ride on the session that
/// answers everybody's reads.
///
/// The read-back is not belt-and-braces. A relational database would refuse a
/// write that broke a constraint; MultiValue has none to break, so a record that
/// is now wrong is still a perfectly valid record. Reading it back and comparing
/// the shape is the only confirmation available, and doing it here means every
/// caller gets it rather than the careful ones.
/// </remarks>
/// <param name="reader">Used to read the record before and after.</param>
/// <param name="options">Where the MCP server is, and how long to wait for it.</param>
/// <param name="logger">For reporting a write that changed the record's shape.</param>
public sealed class ErpWriter(
    IErpReader reader,
    IOptions<ErpConnectionOptions> options,
    ILogger<ErpWriter> logger) : IErpWriter
{
    private readonly IErpReader _reader = reader;
    private readonly ErpConnectionOptions _options = options.Value;
    private readonly ILogger<ErpWriter> _logger = logger;

    /// <inheritdoc />
    public async Task<RecordChange> UpdateValueAsync(
        string fileName,
        string recordId,
        int position,
        int index,
        string value,
        CancellationToken cancellationToken)
    {
        string before = await _reader.ReadRecordAsync(fileName, recordId, cancellationToken);

        await CallAsync(
            new Dictionary<string, object?>
            {
                ["file_name"] = fileName,
                ["record_id"] = recordId,
                ["position"] = position,
                ["index"] = index,
                ["value"] = value,
                // Named here rather than defaulted, because the server treats the
                // absence of this as a question rather than an instruction — and
                // a caller that forgot it would read "confirmation required" as a
                // completed write.
                ["confirm"] = true,
            },
            cancellationToken);

        // Read back from the file rather than trusting what the write returned.
        // "The write succeeded" and "the record changed as intended" are
        // different claims, and only the second one is worth anything.
        string after = await _reader.ReadRecordAsync(fileName, recordId, cancellationToken);

        RecordChange change = new(
            recordId,
            before,
            after,
            FieldLengths(before),
            FieldLengths(after));

        if (!change.IsAlignmentPreserved)
        {
            // Logged as an error because nothing else will notice. The record is
            // well formed either way; what has changed is which branch a quantity
            // belongs to, and no database, screen or later read can tell.
            _logger.LogError(
                "Writing {Record} in {File} changed the record's shape: fields held " +
                "{Before} values and now hold {After}. A parallel field has moved.",
                recordId,
                fileName,
                string.Join(",", change.FieldLengthsBefore),
                string.Join(",", change.FieldLengthsAfter));
        }

        return change;
    }

    /// <summary>
    /// Call the one write tool this application is permitted.
    /// </summary>
    /// <param name="arguments">What to change.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <remarks>
    /// The allowlist is checked here as well as being short, for the same reason
    /// the reader checks its own: a permission that exists only in a comment is
    /// not a permission.
    /// </remarks>
    private async Task CallAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (!ErpWriteTools.Permitted.Contains(ErpWriteTools.UpdateValue))
        {
            throw new InvalidOperationException(
                $"'{ErpWriteTools.UpdateValue}' is not a tool this application may call.");
        }

        HttpClientTransport transport = new(new HttpClientTransportOptions
        {
            Endpoint = _options.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "counter-api-writer",
        });

        using CancellationTokenSource connecting =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connecting.CancelAfter(_options.ConnectBudget);

        await using McpClient client =
            await McpClient.CreateAsync(transport, cancellationToken: connecting.Token);

        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.RequestBudget);

        CallToolResult result = await client.CallToolAsync(
            ErpWriteTools.UpdateValue,
            arguments.ToDictionary(pair => pair.Key, pair => pair.Value),
            cancellationToken: budget.Token);

        // The server reports a refusal in the payload rather than by failing, so
        // a caller that only checked for an exception would treat "write
        // operations disabled" as a completed write.
        System.Text.Json.JsonElement payload = ErpResponse.PayloadFrom(result);

        if (payload.TryGetProperty("error", out System.Text.Json.JsonElement error))
        {
            throw new ErpWriteRefusedException(
                error.GetString() ?? "The ERP refused the change without saying why.");
        }

        if (payload.TryGetProperty("status", out System.Text.Json.JsonElement status) &&
            status.GetString() == "confirmation_required")
        {
            throw new ErpWriteRefusedException(
                "The ERP asked for confirmation, which means this call did not send it. " +
                "Nothing was written.");
        }
    }

    /// <summary>
    /// Count the values in each field of a record.
    /// </summary>
    /// <param name="raw">The record as stored.</param>
    /// <returns>One count per field, in order.</returns>
    /// <remarks>
    /// The shape that has to survive a write. Two records can both be well formed
    /// while one has quietly moved a quantity onto a different branch, and this is
    /// where the difference is visible.
    /// </remarks>
    private static IReadOnlyList<int> FieldLengths(string raw) =>
        raw.Length == 0
            ? []
            : [.. raw.Split(MultiValueRecord.AttributeMark)
                .Select(field => field.Split(MultiValueRecord.ValueMark).Length)];
}

/// <summary>
/// Raised when the ERP declined to make a change.
/// </summary>
/// <remarks>
/// Distinct from a failure to reach the ERP. A refusal means the system worked:
/// writes are switched off, or the change would have damaged the record. The
/// screen says different things for those two situations, and collapsing them
/// would tell somebody to retry when retrying cannot help.
/// </remarks>
public sealed class ErpWriteRefusedException(string message) : Exception(message);
