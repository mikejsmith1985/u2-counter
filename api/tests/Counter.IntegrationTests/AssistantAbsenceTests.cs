namespace Counter.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>
/// A deployment with no assistant, which is a supported way to run this.
/// </summary>
/// <remarks>
/// The assistant needs an API key, and a key is the one thing somebody
/// evaluating this will not have. So running without one has to be an ordinary
/// state rather than a broken one: every other screen works, and the assistant
/// says why it is not there rather than failing when it is asked.
///
/// Nothing exercised it. The suite runs with a key present, and the routes that
/// would answer are never called because calling them costs money -- so the
/// paths that had no coverage were exactly the ones a reader is most likely to
/// meet.
///
/// This builds its own application with the assistant switched off, rather
/// than clearing the key -- the key lives in an environment variable shared by
/// every other test in the process, and the application deliberately never
/// reads it into a variable at all, only checks that it is there.
/// </remarks>
[Collection(CounterCollection.Name)]
public sealed class AssistantAbsenceTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _application;
    private readonly HttpClient _client;

    /// <summary>Start an application that has no assistant configured.</summary>
    /// <param name="fixture">The started services, for the ERP it is already running.</param>
    /// <remarks>
    /// A second application against the suite's own ERP. Starting another one
    /// would be a slower way to get the same answers, and this needs a working
    /// ERP so it can show that everything except the assistant still works.
    /// </remarks>
    public AssistantAbsenceTests(CounterFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // UseSetting rather than ConfigureAppConfiguration, and the
            // difference is timing rather than taste.
            //
            // The application decides whether it has an assistant while it is
            // still building, before the host exists. Configuration added the
            // other way arrives when the host is built, which is afterwards --
            // it works for anything bound through options at run time, like the
            // ERP endpoint below, and silently does nothing for a value read
            // during start-up.
            builder.UseSetting("Assistant:Disabled", "true");

            // Its own audit database, which is not optional.
            //
            // The suite points every application at one SQLite file through a
            // process-wide environment variable. A second application would
            // inherit it and become a second writer on the same file, which is
            // where SQLite's locking assumptions stop holding -- and the tests
            // that would suffer are the ones about the audit trail, which fail
            // intermittently and look like a fault in the thing they cover.
            builder.UseSetting(
                "ConnectionStrings:Counter",
                $"Data Source={Path.Combine(Path.GetTempPath(), $"counter-no-assistant-{Guid.NewGuid():N}.db")}");

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

    [Fact]
    public async Task The_screen_is_told_there_is_no_assistant_before_it_offers_one()
    {
        // Asked on load, so nobody is invited to type into a box that cannot
        // answer. Finding out by asking spends somebody's attention on a
        // feature that is not there.
        JsonElement status = await _client.ReadJsonAsync("/api/v1/ask/status");

        Assert.False(status.GetProperty("isConfigured").GetBoolean());
    }

    [Fact]
    public async Task The_model_is_still_named()
    {
        // The screen says which model would answer. That claim is part of what
        // is being shown and does not depend on a key being present.
        JsonElement status = await _client.ReadJsonAsync("/api/v1/ask/status");

        Assert.False(string.IsNullOrWhiteSpace(status.GetProperty("model").GetString()));
    }

    [Fact]
    public async Task Asking_anyway_is_a_configuration_rather_than_a_fault()
    {
        // 503 rather than 500. One says "this deployment does not have that",
        // the other says "something went wrong", and only the first is true.
        // The screen switches on the difference to choose its message.
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/ask", new { question = "how many are free to sell?" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_refusal_points_back_at_what_does_work()
    {
        // The sentence somebody reads when they meet this. It has to send them
        // back to the rest of the application rather than leave them thinking
        // the deployment is broken.
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/ask", new { question = "anything" });

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Everything else works", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_question_is_refused_too()
    {
        // Nothing typed and no assistant are both reasons not to answer. What
        // matters is that neither reaches the model, because there isn't one.
        using HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/v1/ask", new { question = "   " });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_rest_of_the_application_is_unaffected()
    {
        // The claim the refusal makes, checked rather than repeated. A
        // deployment without a key is not a degraded one; it is this one minus
        // a panel.
        JsonElement files = await _client.ReadJsonAsync("/api/v1/schema/files");

        Assert.NotEmpty(files.GetProperty("files").EnumerateArray());
    }
}
