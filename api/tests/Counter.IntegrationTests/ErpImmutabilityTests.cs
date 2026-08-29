namespace Counter.IntegrationTests;

using System.Text.Json;

/// <summary>
/// The ERP files, byte for byte, before and after everything the application does.
/// </summary>
/// <remarks>
/// T093, and the one test in this suite that must never be cut. Every other
/// read-only guarantee in this application is an assertion about intent: the tool
/// allowlist says which tools may be called, the route tests say no verb accepts
/// a body, the MCP server's own tests say its write paths refuse. All of those
/// describe what the code means to do.
///
/// This one describes what happened. It exercises every journey the application
/// offers and then hashes the files, and a hash that moved is a write nobody
/// intended — whatever the allowlist says, and whichever layer let it through.
///
/// It runs against the copy the fixture made, so a failure here reports a write
/// rather than performing one on the repository's data.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class ErpImmutabilityTests(CounterFixture fixture)
{
    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task Nothing_the_application_does_changes_a_single_byte()
    {
        IReadOnlyDictionary<string, string> before = _fixture.BaselineHashes;

        Assert.NotEmpty(before);

        await ExerciseEveryJourneyAsync();

        IReadOnlyDictionary<string, string> after = _fixture.HashErpData();

        // Reported as three separate findings rather than one inequality, because
        // "the hashes differ" does not tell whoever reads the failure whether
        // something was written, added or deleted.
        Assert.Equal(
            before.Keys.OrderBy(name => name, StringComparer.Ordinal),
            after.Keys.OrderBy(name => name, StringComparer.Ordinal));

        List<string> changed = before
            .Where(entry => after[entry.Key] != entry.Value)
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(
            changed.Count == 0,
            $"The application changed {string.Join(", ", changed)}. It is not read-only.");
    }

    [Fact]
    public async Task A_refused_write_leaves_the_record_exactly_as_it_was()
    {
        // The API's own routing refuses these before any handler runs, so this
        // asserts the outer boundary rather than the server's. The MCP server is
        // never reached, and a comment here once said it was.
        //
        // What makes that acceptable is the test above: it exercises every
        // journey that *does* reach the server and then hashes every file. The
        // server's own refusal is proved in the fork's suite, against the fork.
        string partNumber = await _fixture.Client.AnyPartNumberAsync();

        JsonElement before = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/parts/{partNumber}/record");

        foreach (string method in new[] { "POST", "PUT", "PATCH", "DELETE" })
        {
            using HttpRequestMessage request = new(
                new HttpMethod(method), $"/api/v1/parts/{partNumber}/record");

            using HttpResponseMessage response = await _fixture.Client.SendAsync(request);

            Assert.False(
                response.IsSuccessStatusCode,
                $"{method} against a record was accepted.");
        }

        JsonElement after = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/parts/{partNumber}/record");

        Assert.Equal(
            before.GetProperty("rawRecord").GetString(),
            after.GetProperty("rawRecord").GetString());
    }

    /// <summary>
    /// Do everything the application can be asked to do.
    /// </summary>
    /// <remarks>
    /// The point of the test is the sweep. Hashing after a single search would
    /// prove that searching does not write, which nobody doubted.
    /// </remarks>
    private async Task ExerciseEveryJourneyAsync()
    {
        IReadOnlyList<string> partNumbers = await _fixture.Client.PartNumbersAsync(25);

        JsonElement customers = await _fixture.Client.ReadJsonAsync("/api/v1/customers?q=a");

        string? customerAccount = customers.GetProperty("results").EnumerateArray()
            .Select(customer => customer.GetProperty("accountNumber").GetString())
            .FirstOrDefault();

        foreach (string partNumber in partNumbers)
        {
            JsonElement availability = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/availability" +
                (customerAccount is null ? string.Empty : $"?customerAccount={customerAccount}"));

            await _fixture.Client.ReadJsonAsync($"/api/v1/parts/{partNumber}/record");

            foreach (JsonElement branch in availability.GetProperty("branches").EnumerateArray())
            {
                string branchCode = branch.GetProperty("branchCode").GetString()!;

                using HttpResponseMessage commitments = await _fixture.Client.GetAsync(
                    $"/api/v1/parts/{partNumber}/commitments?branchCode={branchCode}");

                // A branch the BRANCH file does not name is a legitimate 404 here
                // and not a failure of the sweep; the point is that the request
                // was made, not that it succeeded.
                _ = commitments.StatusCode;
            }
        }

        await _fixture.Client.ReadJsonAsync("/api/v1/session");
        await _fixture.Client.ReadJsonAsync("/api/v1/session/personas");
        await _fixture.Client.ReadJsonAsync("/api/v1/activity?limit=50");
    }
}
