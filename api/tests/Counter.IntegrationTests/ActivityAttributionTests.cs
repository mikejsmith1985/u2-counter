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

        IReadOnlyList<ActivityRow> rows = await ReadRowsAsync(marker, subjects.Length);

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

        IReadOnlyList<ActivityRow> rows = await SettledRowsAsync(marker);

        // Two rows would double-count in any report built on this table; none
        // would mean the request left no trace at all.
        Assert.Single(rows);
    }

    [Fact]
    public void Recording_cannot_be_cancelled_by_the_caller()
    {
        // The audit trail's weakest point, asserted where it is decided.
        //
        // The durable write happens after the action has run. Given the
        // request's own cancellation token it would be cancelled by the very
        // thing it exists to record -- a caller who goes away -- so anyone
        // wanting their queries unlogged would only have to stop waiting for the
        // answers.
        //
        // This was first written as a real client cancelling a real request. It
        // passed alone and failed under the full suite, because whether the
        // request reached the server before the cancellation did is a race. A
        // flaky test guarding a compliance claim is worse than none: it fails
        // deployments for no reason until somebody deletes it.
        //
        // So the guarantee moved into the type system instead. RecordAsync takes
        // no cancellation token, and this asserts that -- there is no argument
        // left to get wrong, and no race to lose.
        System.Reflection.MethodInfo record =
            typeof(Counter.Api.Services.ActivityRecorder)
                .GetMethod(nameof(Counter.Api.Services.ActivityRecorder.RecordAsync))!;

        Assert.DoesNotContain(
            record.GetParameters(),
            parameter => parameter.ParameterType == typeof(CancellationToken));
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
    /// Wait for the rows an action produced, then for the count to stop moving.
    /// </summary>
    /// <param name="marker">The term unique to this run.</param>
    /// <returns>The rows, once two reads in a row agree on how many there are.</returns>
    /// <remarks>
    /// For asserting that exactly one row was written. Reading as soon as the
    /// first row lands cannot see a second one still in flight, so the
    /// assertion that a request is not double-counted would pass in precisely
    /// the case it exists to catch.
    /// </remarks>
    private async Task<IReadOnlyList<ActivityRow>> SettledRowsAsync(string marker)
    {
        IReadOnlyList<ActivityRow> rows = await ReadRowsAsync(marker);

        while (true)
        {
            await Task.Delay(250);

            IReadOnlyList<ActivityRow> again = await ReadRowsAsync(marker);

            if (again.Count == rows.Count)
            {
                return again;
            }

            rows = again;
        }
    }

    /// <summary>
    /// Wait for the rows one action produced, and for all of them.
    /// </summary>
    /// <param name="marker">The term unique to this run.</param>
    /// <param name="atLeast">How many rows the caller is going to assert about.</param>
    /// <returns>The rows, or whatever had arrived by the deadline.</returns>
    /// <remarks>
    /// The recording is durable and happens after the response, so a row
    /// arrives shortly after the request that caused it. This waited for the
    /// first row and returned.
    ///
    /// That is enough when one action is being checked and wrong when two are.
    /// The persona test signs in twice and then asserts a row exists for each,
    /// so returning as soon as either had landed made it fail whenever the
    /// second write was still in flight -- which is under load, which is in the
    /// full suite and never on its own. A test that passes alone and fails in
    /// company is worse than one that fails always: it teaches whoever sees the
    /// red to run it again rather than to read it.
    /// </remarks>
    private async Task<IReadOnlyList<ActivityRow>> ReadRowsAsync(string marker, int atLeast = 1)
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

            // Returned short only at the deadline, so a genuine absence still
            // fails the assertion that wanted the row rather than hanging.
            if (rows.Count >= atLeast || DateTime.UtcNow > deadline)
            {
                return rows;
            }

            await Task.Delay(100);
        }
    }
}
