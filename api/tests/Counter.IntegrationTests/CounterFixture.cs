namespace Counter.IntegrationTests;

using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

/// <summary>
/// Everything the integration tests run against, started once for the suite.
/// </summary>
/// <remarks>
/// Real infrastructure, not stand-ins. The MCP server is the hardened fork,
/// started as a process and reached over HTTP exactly as the application reaches
/// it in production. SQL Server is a container. Nothing here is mocked, because a
/// mock of the MCP server would prove that the application can talk to a mock.
///
/// The ERP data is copied to a temporary directory first. That is what allows
/// <see cref="ErpImmutabilityTests"/> to hash every file before and after the
/// suite and prove read-only by outcome — and it means a test that did write
/// something would damage a copy rather than the repository's data.
/// </remarks>
public sealed class CounterFixture : IAsyncLifetime
{
    /// <summary>How long to wait for the MCP server to start listening.</summary>
    private static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(60);

    /// <summary>How often to check whether it has.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly MsSqlContainer _sql =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private Process? _mcpServer;
    private string _dataDirectory = string.Empty;

    /// <summary>The application under test, wired to the started services.</summary>
    public WebApplicationFactory<Program> Application { get; private set; } = null!;

    /// <summary>An HTTP client against the application.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>The copied ERP data this run reads.</summary>
    public string DataDirectory => _dataDirectory;

    /// <summary>Where the repository keeps this feature's files.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// A hash of every ERP file as it was before any test ran.
    /// </summary>
    /// <remarks>
    /// Taken before the server starts, so it is a record of the data as copied
    /// rather than as the server first found it. Comparing against this is what
    /// turns "the application is read-only" from a claim into a measurement.
    /// </remarks>
    public IReadOnlyDictionary<string, string> BaselineHashes { get; private set; } =
        new Dictionary<string, string>();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _dataDirectory = CopyErpData();
        BaselineHashes = HashErpData();

        await _sql.StartAsync();

        // Set on the process rather than passed to the factory, because the
        // application reads its connection string while building the host and a
        // configuration source added afterwards arrives too late to be seen. An
        // environment variable is in place before the entry point runs, which is
        // the only thing that is.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Counter", _sql.GetConnectionString());

        int mcpPort = FindFreePort();
        _mcpServer = StartMcpServer(mcpPort, _dataDirectory);

        await WaitForListenerAsync(mcpPort);

        Application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Erp:Endpoint"] = $"http://127.0.0.1:{mcpPort}/",
                    ["Erp:RequestBudget"] = "00:00:10",
                })));

        Client = Application.CreateClient();

        // The catalogue is built in the background at startup. A test that
        // searched before it finished would see an empty result and read as a
        // failure of search rather than of timing.
        await WaitForCatalogueAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        Client?.Dispose();

        if (Application is not null)
        {
            await Application.DisposeAsync();
        }

        StopMcpServer();

        Environment.SetEnvironmentVariable("ConnectionStrings__Counter", null);

        await _sql.DisposeAsync();

        TryDeleteDataDirectory();
    }

    /// <summary>
    /// Return a SHA-256 hash of every ERP file, keyed by file name.
    /// </summary>
    /// <remarks>
    /// Content hashes rather than timestamps: a write that happened to restore
    /// the same bytes is not a change anyone can observe, and a timestamp that
    /// moved without the bytes moving is not one either.
    /// </remarks>
    public IReadOnlyDictionary<string, string> HashErpData()
    {
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(_dataDirectory).OrderBy(name => name, StringComparer.Ordinal))
        {
            using FileStream stream = File.OpenRead(path);
            hashes[Path.GetFileName(path)] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return hashes;
    }

    /// <summary>Copy the ERP files somewhere a test run cannot damage them.</summary>
    private static string CopyErpData()
    {
        string source = Path.Combine(RepositoryRoot, "mvstore", "data");

        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"The demonstration data was not found at {source}. Run the seeder first.");
        }

        string destination = Path.Combine(
            Path.GetTempPath(),
            "counter-tests-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        return destination;
    }

    /// <summary>
    /// Start the hardened MCP server against the copied data.
    /// </summary>
    /// <remarks>
    /// The fork's own installed entry point, not its source imported into a test
    /// host. What is being demonstrated is a server someone can run; running it
    /// any other way here would prove something else.
    /// </remarks>
    private static Process StartMcpServer(int port, string dataDirectory)
    {
        string forkRoot = Environment.GetEnvironmentVariable("U2_MCP_ROOT")
            ?? Path.Combine(Path.GetDirectoryName(RepositoryRoot)!, "u2-mcp");

        string executable = Path.Combine(forkRoot, ".venv", "Scripts", "u2-mcp.exe");

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                $"The hardened MCP server was not found at {executable}. " +
                "Set U2_MCP_ROOT to where the fork is checked out.");
        }

        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("--streamable-http");
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add("127.0.0.1");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));

        start.Environment["U2_DRIVER"] = "demo";
        start.Environment["MVSTORE_DATA_PATH"] = dataDirectory;
        start.Environment["PYTHONPATH"] = Path.Combine(RepositoryRoot, "mvstore", "src");

        // The server validates its connection settings at startup whichever
        // driver is configured, so these have to be present even though the demo
        // driver never authenticates against anything. Placeholders, not
        // credentials: there is no Universe instance behind them.
        start.Environment["U2_HOST"] = "127.0.0.1";
        start.Environment["U2_USER"] = "u2demo";
        start.Environment["U2_PASSWORD"] = "demo-no-database-behind-this";
        start.Environment["U2_ACCOUNT"] = "DEMO";

        Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The MCP server did not start.");

        // Drained so a full pipe buffer cannot block the server mid-suite, which
        // would look like the ERP hanging rather than like a test-host mistake.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>Stop the server by its own process id, never by name.</summary>
    /// <remarks>
    /// Article II. A pattern-matched stop would end unrelated Python processes,
    /// including ones belonging to whoever is running the suite.
    /// </remarks>
    private void StopMcpServer()
    {
        if (_mcpServer is null)
        {
            return;
        }

        try
        {
            if (!_mcpServer.HasExited)
            {
                _mcpServer.Kill(entireProcessTree: true);
                _mcpServer.WaitForExit((int)TimeSpan.FromSeconds(10).TotalMilliseconds);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone, which is the outcome wanted.
        }
        finally
        {
            _mcpServer.Dispose();
            _mcpServer = null;
        }
    }

    /// <summary>Remove the copied data, tolerating a file still held open.</summary>
    private void TryDeleteDataDirectory()
    {
        if (string.IsNullOrEmpty(_dataDirectory) || !Directory.Exists(_dataDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is untidy, not a failure. Reporting
            // it as one would fail a suite that had otherwise passed.
        }
    }

    /// <summary>Wait until something is listening on the port.</summary>
    private static async Task WaitForListenerAsync(int port)
    {
        DateTime deadline = DateTime.UtcNow + StartupBudget;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using TcpClient probe = new();
                await probe.ConnectAsync("127.0.0.1", port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(PollInterval);
            }
        }

        throw new TimeoutException(
            $"The MCP server was not listening on port {port} within {StartupBudget.TotalSeconds:0} seconds.");
    }

    /// <summary>Wait until search can answer, meaning the catalogue is built.</summary>
    private async Task WaitForCatalogueAsync()
    {
        DateTime deadline = DateTime.UtcNow + StartupBudget;

        while (DateTime.UtcNow < deadline)
        {
            using HttpResponseMessage response = await Client.GetAsync("/api/v1/parts?q=breaker&limit=1");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();

                if (body.Contains("\"partNumber\"", StringComparison.Ordinal))
                {
                    return;
                }
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException("The catalogue was not readable within the startup budget.");
    }

    /// <summary>Ask the operating system for a port nothing is using.</summary>
    /// <remarks>
    /// A fixed port would collide with a development server someone left running,
    /// and the failure would look like the MCP server refusing to start.
    /// </remarks>
    private static int FindFreePort()
    {
        using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    /// <summary>Walk up from the test assembly until the feature root appears.</summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "mvstore")) &&
                Directory.Exists(Path.Combine(directory.FullName, "api")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "The repository root was not found above the test assembly.");
    }
}

/// <summary>
/// Binds every integration test to one set of started services.
/// </summary>
/// <remarks>
/// One collection rather than one fixture per class: starting a container and a
/// server takes tens of seconds, and a suite nobody will wait for is a suite
/// nobody runs.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class CounterCollection : ICollectionFixture<CounterFixture>
{
    /// <summary>The collection name each test class declares.</summary>
    public const string Name = "counter";
}
