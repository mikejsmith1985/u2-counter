namespace Counter.IntegrationTests;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// A server that accepts a connection and then says nothing.
/// </summary>
/// <remarks>
/// The failure this covers took the deployed application down completely, and it
/// is not exotic: the ERP behind this is a container that scales to zero, so an
/// endpoint that accepts a connection and then takes half a minute to answer is
/// the ordinary case rather than a contrived one.
///
/// `McpClient.CreateAsync` performs a handshake, and against such an endpoint it
/// waited indefinitely — it was the one call in the reader without a budget. The
/// catalogue build holds a lock while it runs, so the unbounded connect held that
/// lock forever, and every later search queued behind it. Requests did not fail
/// slowly; they never returned at all.
///
/// A refused connection fails immediately and was already covered. This is the
/// case that looks like success right up until it does not.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class ConnectBudgetTests(CounterFixture fixture)
{
    /// <summary>The budget the application is given, for this test.</summary>
    private static readonly TimeSpan ConnectBudget = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How far past the budget an answer may still arrive.
    /// </summary>
    /// <remarks>
    /// Wide, because what is being asserted is that the wait ends at all. A
    /// failure here means the request never returned, and the number it returned
    /// after is not the point.
    /// </remarks>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(10);

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task A_server_that_accepts_and_says_nothing_does_not_hang_the_request()
    {
        using SilentListener silent = SilentListener.Start();

        (IAsyncDisposable application, HttpClient client) =
            _fixture.ApplicationAgainst(silent.Port, TimeSpan.FromSeconds(2), ConnectBudget);

        await using ConfiguredPair _ = new(application, client);

        Stopwatch timer = Stopwatch.StartNew();
        using HttpResponseMessage response =
            await client.GetAsync("/api/v1/parts/S-BRK00000/availability");
        timer.Stop();

        Assert.True(
            timer.Elapsed < ConnectBudget + Margin,
            $"The request took {timer.Elapsed.TotalSeconds:0.0}s against a " +
            $"{ConnectBudget.TotalSeconds:0}s connect budget. Nothing bounded the handshake, " +
            "which is how every search on the deployed application came to hang.");

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_second_request_is_not_queued_behind_the_first()
    {
        // The consequence that made this fatal rather than annoying. One stuck
        // connect held the catalogue lock, so requests arriving afterwards
        // waited on the lock rather than on their own budget — and a client
        // giving up did nothing to release it.
        using SilentListener silent = SilentListener.Start();

        (IAsyncDisposable application, HttpClient client) =
            _fixture.ApplicationAgainst(silent.Port, TimeSpan.FromSeconds(2), ConnectBudget);

        await using ConfiguredPair _ = new(application, client);

        using (await client.GetAsync("/api/v1/parts?q=breaker"))
        {
            // The first one is expected to fail; what matters is the second.
        }

        Stopwatch timer = Stopwatch.StartNew();
        using HttpResponseMessage second = await client.GetAsync("/api/v1/parts?q=wire");
        timer.Stop();

        Assert.True(
            timer.Elapsed < ConnectBudget + Margin,
            $"The second request took {timer.Elapsed.TotalSeconds:0.0}s. It was waiting on the " +
            "first rather than on its own budget.");
    }
}

/// <summary>
/// A socket that accepts connections and never answers.
/// </summary>
/// <remarks>
/// Not a refusal, which is a different failure and an easier one: this completes
/// the TCP handshake, so every layer above believes it has a connection, and then
/// nothing arrives.
/// </remarks>
internal sealed class SilentListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<TcpClient> _accepted = [];

    private SilentListener(TcpListener listener)
    {
        _listener = listener;
    }

    /// <summary>The port it is listening on.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Begin accepting, and holding, connections.</summary>
    public static SilentListener Start()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        SilentListener silent = new(listener);

        _ = Task.Run(async () =>
        {
            while (!silent._stopping.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(silent._stopping.Token);

                    // Held open and never written to. Closing it would be a
                    // different failure -- one the reader already handles.
                    lock (silent._accepted)
                    {
                        silent._accepted.Add(client);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
            }
        });

        return silent;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();

        lock (_accepted)
        {
            foreach (TcpClient client in _accepted)
            {
                client.Dispose();
            }
        }

        _listener.Stop();
        _stopping.Dispose();
    }
}

/// <summary>Disposes an application and its client together.</summary>
internal sealed class ConfiguredPair(IAsyncDisposable application, HttpClient client)
    : IAsyncDisposable
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await application.DisposeAsync();
    }
}
