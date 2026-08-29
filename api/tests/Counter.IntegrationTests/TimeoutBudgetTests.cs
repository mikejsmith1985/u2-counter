namespace Counter.IntegrationTests;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Counter.Api.Filters;

/// <summary>
/// A slow ERP is reported as unreachable, within the budget, and never as empty.
/// </summary>
/// <remarks>
/// SC-013. This is the failure the whole design is built around and the one
/// hardest to provoke honestly: a database that has not gone away but has stopped
/// answering. A refused connection is a different thing, easy to arrange and
/// covered by <see cref="FailureShapeTests"/>; the interesting case is the ERP
/// that is up and slow, because that is when an application is most tempted to
/// keep waiting and then return nothing.
///
/// The slowness is switched on after the application is healthy, not before it
/// starts. An application still loading its catalogue is not the application a
/// user meets, and a budget measured against that state is a budget measured
/// against a state nobody is in — the first version of this test did exactly
/// that, and reported ten seconds against a two-second budget while the budget
/// was working correctly.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class TimeoutBudgetTests(CounterFixture fixture)
{
    /// <summary>The budget the application is given, for this test.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <summary>How long each read takes once the store is made slow.</summary>
    private static readonly TimeSpan StoreDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How far past the budget a response may still arrive.
    /// </summary>
    /// <remarks>
    /// The budget bounds one ERP call and a screen makes several, so a request
    /// can legitimately exceed it. What is checked is that the answer comes back
    /// nearer the budget than the fifteen seconds one read was told to take — so
    /// the margin is wide, and the assertion still fails loudly if nothing
    /// bounded the wait at all.
    /// </remarks>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(6);

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task The_budget_under_test_is_the_one_configured()
    {
        // Without this, every assertion below could pass or fail because the
        // override never reached the application — a mistake in the test that
        // would read as a mistake in the code.
        await using SlowErp slow = await SlowErp.StartAsync(_fixture, Budget);

        JsonElement health = await slow.Client.ReadJsonAsync("/health");

        Assert.Equal(Budget.TotalSeconds, health.GetProperty("requestBudgetSeconds").GetDouble());
        Assert.True(health.GetProperty("isReady").GetBoolean());
    }

    [Fact]
    public async Task A_slow_erp_returns_unreachable_inside_the_budget()
    {
        await using SlowErp slow = await SlowErp.StartAsync(_fixture, Budget);
        slow.BecomeSlow(StoreDelay);

        Stopwatch timer = Stopwatch.StartNew();
        using HttpResponseMessage response =
            await slow.Client.GetAsync("/api/v1/parts/S-BRK00000/availability");
        timer.Stop();

        Assert.False(
            response.IsSuccessStatusCode,
            "A slow ERP returned a successful answer. An answer nobody waited for is " +
            "worse than none, because the screen presents it as fact.");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);

        JsonElement problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(ErpProblemFilter.UnreachableType, problem.GetProperty("type").GetString());

        Assert.True(
            timer.Elapsed < Budget + Margin,
            $"The request took {timer.Elapsed.TotalSeconds:0.0}s against a " +
            $"{Budget.TotalSeconds:0}s budget, where one read was told to take " +
            $"{StoreDelay.TotalSeconds:0}s. Nothing bounded the wait.");
    }

    [Fact]
    public async Task A_slow_erp_never_produces_an_empty_result()
    {
        // The distinction that matters more than the timing. A representative
        // reading an empty result tells a customer there is no stock; one reading
        // "the system could not be reached" rings them back.
        await using SlowErp slow = await SlowErp.StartAsync(_fixture, Budget);
        slow.BecomeSlow(StoreDelay);

        using HttpResponseMessage response =
            await slow.Client.GetAsync("/api/v1/parts/S-BRK00000/availability");

        string body = await response.Content.ReadAsStringAsync();

        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain("\"branches\":[]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"isStockKnown\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_application_recovers_once_the_erp_stops_being_slow()
    {
        // A timed-out request abandons the shared MCP connection. Left in place,
        // one slow moment would break the application until someone restarted
        // it — a far worse outcome than the slowness that caused it.
        await using SlowErp slow = await SlowErp.StartAsync(_fixture, Budget);

        slow.BecomeSlow(StoreDelay);

        using (HttpResponseMessage timedOut =
            await slow.Client.GetAsync("/api/v1/parts/S-BRK00000/availability"))
        {
            Assert.Equal(HttpStatusCode.GatewayTimeout, timedOut.StatusCode);
        }

        slow.BecomeFast();

        // Polled rather than asserted once. The MCP server holds a single
        // database session, and the read this test abandoned is still occupying
        // it until the fifteen seconds it was told to take have elapsed. That is
        // correct behaviour -- one session, one command -- and the thing worth
        // asserting is that the application comes back on its own, not that it
        // comes back instantly.
        bool hasRecovered = await slow.RecoversWithinAsync(
            StoreDelay + TimeSpan.FromSeconds(15));

        Assert.True(
            hasRecovered,
            "The application never recovered after a timeout. One slow moment left it broken.");
    }
}

/// <summary>
/// An MCP server that can be made slow while it runs, and a client against it.
/// </summary>
/// <remarks>
/// A second server rather than the suite's shared one, because slowing that one
/// would slow every other test in the collection and make the order they run in
/// matter. It reads the same copied data, which is safe because nothing writes.
/// </remarks>
internal sealed class SlowErp : IAsyncDisposable
{
    private readonly IDisposable _server;
    private readonly IAsyncDisposable _application;
    private readonly HttpClient _client;
    private readonly string _switchPath;

    private SlowErp(
        IDisposable server,
        IAsyncDisposable application,
        HttpClient client,
        string switchPath)
    {
        _server = server;
        _application = application;
        _client = client;
        _switchPath = switchPath;
    }

    /// <summary>A client whose requests reach this server.</summary>
    public HttpClient Client => _client;

    /// <summary>
    /// Start a server that answers promptly, and an application ready to use it.
    /// </summary>
    /// <param name="fixture">The suite's services, for the paths it knows.</param>
    /// <param name="budget">How long the application should wait for a read.</param>
    public static async Task<SlowErp> StartAsync(CounterFixture fixture, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        (IDisposable server, int port, string switchPath) =
            await fixture.StartSwitchableServerAsync();

        (IAsyncDisposable application, HttpClient client) =
            fixture.ApplicationAgainst(port, budget);

        SlowErp erp = new(server, application, client, switchPath);

        // Ready before anything is measured. What is under test is a healthy
        // application meeting a database that has stopped answering, not an
        // application that never became healthy.
        await erp.WaitUntilReadyAsync();

        return erp;
    }

    /// <summary>Make every subsequent read take this long.</summary>
    /// <param name="delay">How long a read should take.</param>
    public void BecomeSlow(TimeSpan delay) => File.WriteAllText(
        _switchPath,
        ((int)delay.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

    /// <summary>Answer promptly again.</summary>
    public void BecomeFast()
    {
        if (File.Exists(_switchPath))
        {
            File.Delete(_switchPath);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        BecomeFast();
        _client.Dispose();
        await _application.DisposeAsync();
        _server.Dispose();
    }

    /// <summary>
    /// Keep asking until the application answers, or until time runs out.
    /// </summary>
    /// <param name="within">How long to keep trying.</param>
    /// <returns>Whether it recovered.</returns>
    public async Task<bool> RecoversWithinAsync(TimeSpan within)
    {
        DateTime deadline = DateTime.UtcNow + within;

        while (DateTime.UtcNow < deadline)
        {
            using HttpResponseMessage response =
                await _client.GetAsync("/api/v1/parts/S-BRK00000/availability");

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    /// <summary>Wait until the catalogue is loaded and search can answer.</summary>
    private async Task WaitUntilReadyAsync()
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);

        while (DateTime.UtcNow < deadline)
        {
            using HttpResponseMessage response = await _client.GetAsync("/health");

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("The application never became ready.");
    }
}
