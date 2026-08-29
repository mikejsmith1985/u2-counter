namespace Counter.Infrastructure.Mcp;

using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// Reads the ERP through the hardened MCP server.
/// </summary>
/// <remarks>
/// Reading through the server rather than a database driver is the load-bearing
/// decision of this architecture. The server already enforces read-only access,
/// an allowlist of query verbs, per-caller identity and an audit trail, with
/// tests proving each; reaching past it to a driver would mean rebuilding all of
/// that here, worse.
///
/// This class calls only the tools in <see cref="ErpTools.Permitted"/>. That is
/// asserted by test rather than left to discipline.
/// </remarks>
public sealed class ErpReader : IErpReader, IAsyncDisposable
{
    private readonly ErpConnectionOptions _options;
    private readonly ILogger<ErpReader> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private McpClient? _client;

    /// <summary>
    /// Create a reader.
    /// </summary>
    /// <param name="options">Where the MCP server is and how long to wait for it.</param>
    /// <param name="logger">For recording connection and failure events.</param>
    public ErpReader(IOptions<ErpConnectionOptions> options, ILogger<ErpReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ReadRecordAsync(
        string fileName,
        string recordId,
        CancellationToken cancellationToken)
    {
        JsonElement result = await CallAsync(
            ErpTools.ReadRecord,
            new Dictionary<string, object?>
            {
                ["file_name"] = fileName,
                ["record_id"] = recordId,
            },
            cancellationToken);

        return ErpResponse.RawRecordFrom(result, fileName, recordId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SelectKeysAsync(
        string query,
        int maxKeys,
        CancellationToken cancellationToken)
    {
        JsonElement result = await CallAsync(
            ErpTools.GetSelectList,
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["max_ids"] = maxKeys,
            },
            cancellationToken);

        return ErpRecords.RecordIdsFrom(result);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ReadRecordsAsync(
        string fileName,
        IReadOnlyList<string> recordIds,
        CancellationToken cancellationToken)
    {
        if (recordIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        JsonElement result = await CallAsync(
            ErpTools.ReadRecords,
            new Dictionary<string, object?>
            {
                ["file_name"] = fileName,
                ["record_ids"] = recordIds,
            },
            cancellationToken);

        return ErpRecords.RecordsFrom(result);
    }

    /// <inheritdoc />
    public async Task<ErpQueryResult> QueryAsync(
        string query,
        int maxRows,
        CancellationToken cancellationToken)
    {
        JsonElement result = await CallAsync(
            ErpTools.ExecuteQuery,
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["max_rows"] = maxRows,
            },
            cancellationToken);

        return ErpResponse.QueryResultFrom(result, query);
    }

    /// <summary>
    /// Call one permitted tool.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If a caller names a tool outside the permitted set. This cannot happen
    /// through the methods above; it guards against a future one being added
    /// without thought.
    /// </exception>
    /// <exception cref="ErpUnreachableException">
    /// If the server does not answer within the configured budget.
    /// </exception>
    private async Task<JsonElement> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (!ErpTools.Permitted.Contains(toolName))
        {
            throw new InvalidOperationException(
                $"'{toolName}' is not a tool this application may call.");
        }

        McpClient client = await ConnectAsync(cancellationToken);

        // The budget is enforced here as well as at the server. The server's
        // protects the database from an abandoned query; this one protects the
        // person waiting. Either alone leaves a gap the other covers.
        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.RequestBudget);

        try
        {
            CallToolResult result = await client.CallToolAsync(
                toolName,
                arguments.ToDictionary(pair => pair.Key, pair => pair.Value),
                cancellationToken: budget.Token);

            return ErpResponse.PayloadFrom(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "The ERP did not answer {Tool} within {Budget}", toolName, _options.RequestBudget);

            // Dropped, so the next request opens a fresh one. A connection whose
            // last exchange was abandoned mid-stream cannot be assumed usable.
            await DiscardConnectionAsync();

            throw new ErpUnreachableException(
                $"The ERP did not answer within {_options.RequestBudget.TotalSeconds:0} seconds.");
        }
        catch (Exception error) when (IsConnectionFailure(error))
        {
            // A refused or dropped connection is the same situation as a timeout
            // from the caller's side, and must arrive as the same answer. Letting
            // it through as a server error would give the screen nothing to
            // distinguish from a genuine result -- which is precisely how a
            // representative ends up telling a customer there is no stock.
            _logger.LogWarning(error, "The ERP could not be reached for {Tool}", toolName);

            await DiscardConnectionAsync();

            throw new ErpUnreachableException("The system could not reach the stock data.");
        }
    }

    /// <summary>
    /// Whether a failure means the server could not be reached.
    /// </summary>
    /// <remarks>
    /// The inner exceptions are inspected because the transport wraps a socket
    /// failure in its own type, and the outer type alone says only that
    /// something went wrong rather than what.
    /// </remarks>
    private static bool IsConnectionFailure(Exception error)
    {
        for (Exception? candidate = error; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is HttpRequestException or SocketException or IOException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Drop the shared connection so the next caller opens a new one.
    /// </summary>
    /// <remarks>
    /// Taken under the same gate that guards connecting: without it, one thread
    /// could be disposing the client another had just decided to reuse.
    /// </remarks>
    private async Task DiscardConnectionAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            McpClient? dead = _client;
            _client = null;

            if (dead is not null)
            {
                await dead.DisposeAsync();
            }
        }
#pragma warning disable CA1031 // Disposing a broken connection must not raise.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Whatever went wrong closing a connection already known to be
            // broken, the useful outcome -- that nobody will reuse it -- has
            // already happened.
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>
    /// Connect on first use, and share one connection afterwards.
    /// </summary>
    /// <remarks>
    /// Connecting is serialized: a burst of requests arriving before the first
    /// connection completes would otherwise each open one, which is the same
    /// leak the MCP server itself was hardened against.
    /// </remarks>
    private async Task<McpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            _logger.LogInformation("Connecting to the MCP server at {Endpoint}", _options.Endpoint);

            HttpClientTransport transport = new(new HttpClientTransportOptions
            {
                Endpoint = _options.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "counter-api",
            });

            try
            {
                _client = await McpClient.CreateAsync(
                    transport, cancellationToken: cancellationToken);
            }
            catch (Exception error) when (IsConnectionFailure(error))
            {
                // Failing to connect at all is the plainest form of unreachable,
                // and the one a demonstration hits first when the server is not
                // running. It must not surface as a server error.
                _logger.LogWarning(
                    error, "The MCP server at {Endpoint} could not be reached", _options.Endpoint);

                throw new ErpUnreachableException("The system could not reach the stock data.");
            }

            return _client;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _connectionGate.Dispose();
    }
}
