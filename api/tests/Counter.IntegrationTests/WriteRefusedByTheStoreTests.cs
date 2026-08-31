namespace Counter.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>
/// A deployment that permits writes, against a store that does not.
/// </summary>
/// <remarks>
/// The suite's own deployment has no write path at all, so the endpoint answers
/// 501 and the code behind it never runs. The browser suite drives the real
/// thing against a writable stack, which is the right place for the screen and
/// the wrong place for the API's contract.
///
/// What was left uncovered is the middle case, and it is the one that happens
/// in a real account: writes are configured, and the database declines this
/// particular one. That has to arrive as a conflict rather than a fault --
/// retrying will not help and the screen should not offer to -- and it must
/// carry the reason, because "the change was refused" without one sends
/// somebody to look at the wrong thing.
///
/// Arranged by pointing a writable application at the read-only store the suite
/// already runs. Nothing is mocked: the refusal comes from the store, travels
/// back through the MCP server as a payload rather than an exception, and the
/// application has to notice.
/// </remarks>
/// <param name="fixture">The started services, for the ERP it is already running.</param>
[Collection(CounterCollection.Name)]
public sealed class WriteRefusedByTheStoreTests : IDisposable
{
    private readonly CounterFixture _fixture;
    private readonly WebApplicationFactory<Program> _application;
    private readonly HttpClient _client;

    /// <summary>Start an application that believes it may write.</summary>
    /// <param name="fixture">The started services.</param>
    public WriteRefusedByTheStoreTests(CounterFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;

        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Read while the application is still building, so it has to be a
            // host setting rather than configuration added afterwards.
            builder.UseSetting("Erp:Writable", "true");

            // Its own audit database. The suite points every application at one
            // file through a process-wide variable, and a second writer on one
            // SQLite file is where its locking assumptions stop holding.
            builder.UseSetting(
                "ConnectionStrings:Counter",
                $"Data Source={Path.Combine(Path.GetTempPath(), $"counter-writable-{Guid.NewGuid():N}.db")}");

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Erp:Endpoint"] = $"http://127.0.0.1:{fixture.McpPort}/",
                    ["Erp:RequestBudget"] = "00:00:10",
                }));
        });

        _client = _application.CreateClient();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
        _application.Dispose();
    }

    /// <summary>Ask to change one value of one record.</summary>
    /// <param name="value">What to put there.</param>
    /// <returns>The response.</returns>
    private Task<HttpResponseMessage> ChangeAValueAsync(string value) =>
        _client.PostAsJsonAsync(
            "/api/v1/records/BRANCH/AUR/value",
            new { position = 1, index = 0, value });

    [Fact]
    public async Task This_deployment_reports_that_it_can_write()
    {
        // The premise of everything below. Without it the endpoint answers 501
        // and none of the code under test runs.
        var status = await _client.GetFromJsonAsync<Dictionary<string, bool>>(
            "/api/v1/records/status");

        Assert.True(status!["canWrite"]);
    }

    [Fact]
    public async Task A_store_that_declines_produces_a_conflict_rather_than_an_error()
    {
        // The distinction that matters. A fault says "try again"; a conflict
        // says "this will not work", and only the second is true here.
        using HttpResponseMessage response = await ChangeAValueAsync("AURORA");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_refusal_carries_the_reason_the_store_gave()
    {
        // "The change was refused" with no reason sends somebody to look at the
        // wrong thing. The store's own sentence travels the whole way back.
        using HttpResponseMessage response = await ChangeAValueAsync("AURORA");

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("read-only", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_refusal_carries_a_type_the_screen_can_switch_on()
    {
        // What the client actually reads. It parses the body and chooses its
        // message from `type`, so that field is the contract -- a refusal the
        // screen has to recognise by its wording would break on a reword.
        using HttpResponseMessage response = await ChangeAValueAsync("AURORA");

        JsonElement problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("change-refused", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_value_carrying_a_separator_is_refused_before_the_store_is_asked()
    {
        // The guard in front of the write, reached through the whole stack. An
        // attribute mark would split one field into two, and every field after
        // it would then describe something it is not.
        using HttpResponseMessage response = await ChangeAValueAsync($"one{(char)254}two");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("shape", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nothing_reached_the_erp_data()
    {
        // The check worth more than the status code. A refused write that had
        // already touched the file would be worse than one that succeeded,
        // because nobody would go looking.
        using (await ChangeAValueAsync("AURORA"))
        {
            // The response is asserted elsewhere; this is about the files.
        }

        Assert.Equal(_fixture.BaselineHashes, _fixture.HashErpData());
    }
}
