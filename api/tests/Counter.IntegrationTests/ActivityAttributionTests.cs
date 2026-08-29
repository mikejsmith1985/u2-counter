namespace Counter.IntegrationTests;

using System.Net.Http.Json;
using System.Text.Json;
using Counter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Every request is recorded against the person who made it.
/// </summary>
/// <remarks>
/// SC-006. The reason this matters is stated plainly on the governance strip: the
/// application signs in to the database as one shared account, so the database's
/// own log can only ever say "u2demo did it". If this application does not name
/// the person, nothing does — and an audit trail that cannot name a person is a
/// debugging aid.
///
/// The assertions read the durable table rather than the activity endpoint. The
/// endpoint shows a person their own recent work; a reviewer reads the table, and
/// it is the table that has to be right.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class ActivityAttributionTests(CounterFixture fixture)
{
    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task Each_persona_gets_its_own_rows_naming_it_and_the_shared_login()
    {
        JsonElement personas = await _fixture.Client.ReadJsonAsync("/api/v1/session/personas");

        string[] subjects = personas.EnumerateArray()
            .Select(persona => persona.GetProperty("subject").GetString()!)
            .Take(2)
            .ToArray();

        Assert.True(subjects.Length >= 2, "The demonstration needs at least two personas.");

        string marker = "attrib-" + Guid.NewGuid().ToString("N")[..8];

        foreach (string subject in subjects)
        {
            using HttpClient browser = NewBrowser();

            using HttpResponseMessage signIn = await browser.PostAsJsonAsync(
                "/api/v1/session", new { subject });

            signIn.EnsureSuccessStatusCode();

            // A search whose term is unique to this run, so the rows it produces
            // can be found among everything else the suite did.
            await browser.GetAsync($"/api/v1/parts?q={marker}-{subject}");
        }

        IReadOnlyList<ActivityRow> rows = await ReadRowsAsync(marker);

        foreach (string subject in subjects)
        {
            ActivityRow[] mine = rows.Where(row => row.UserSubject == subject).ToArray();

            Assert.True(mine.Length > 0, $"Nothing was recorded for {subject}.");

            foreach (ActivityRow row in mine)
            {
                Assert.False(string.IsNullOrWhiteSpace(row.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(row.DatabaseLogin));

                // The whole point of recording the login beside the person: a
                // reviewer has to be able to see that the database could not tell
                // these two callers apart.
                Assert.True(row.DatabaseLoginIsShared);
            }
        }

        // No row may be attributed to a persona that did not make it.
        Assert.All(rows, row => Assert.Contains(row.UserSubject, subjects));
    }

    [Fact]
    public async Task A_request_that_failed_is_recorded_as_having_failed()
    {
        // A failure nobody recorded is indistinguishable from a request nobody
        // made, and "why did the screen say no stock" is exactly the question a
        // reviewer asks after the fact.
        string marker = "MISSING-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        using HttpClient browser = NewBrowser();

        using HttpResponseMessage response =
            await browser.GetAsync($"/api/v1/parts/{marker}/availability");

        Assert.False(response.IsSuccessStatusCode);

        IReadOnlyList<ActivityRow> rows = await ReadRowsAsync(marker);

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal("NotFound", row.Outcome));
    }

    [Fact]
    public async Task One_request_produces_exactly_one_row()
    {
        string marker = "once-" + Guid.NewGuid().ToString("N")[..8];

        using HttpClient browser = NewBrowser();
        await browser.GetAsync($"/api/v1/parts?q={marker}");

        IReadOnlyList<ActivityRow> rows = await ReadRowsAsync(marker);

        // Two rows would double-count in any report built on this table; none
        // would mean the request left no trace at all.
        Assert.Single(rows);
    }

    /// <summary>A client with its own cookie jar, so it is its own session.</summary>
    /// <remarks>
    /// The fixture's shared client keeps no cookies, so every request through it
    /// is a new browser. A test about who did what needs the opposite.
    /// </remarks>
    private HttpClient NewBrowser() =>
        _fixture.Application.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

    /// <summary>
    /// Read the durable rows whose target carries a marker.
    /// </summary>
    /// <remarks>
    /// The audit write does not block the response, so a read immediately after
    /// one can legitimately find nothing yet. Polling to a deadline distinguishes
    /// "not written yet" from "not written", which a single read cannot.
    /// </remarks>
    private async Task<IReadOnlyList<ActivityRow>> ReadRowsAsync(string marker)
    {
        IDbContextFactory<CounterContext> contexts = _fixture.Application.Services
            .GetRequiredService<IDbContextFactory<CounterContext>>();

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (true)
        {
            await using CounterContext context = await contexts.CreateDbContextAsync();

            List<ActivityRow> rows = await context.Activity
                .Where(row => row.TargetKey.Contains(marker))
                .ToListAsync();

            if (rows.Count > 0 || DateTime.UtcNow > deadline)
            {
                return rows;
            }

            await Task.Delay(100);
        }
    }
}
