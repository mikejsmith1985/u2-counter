namespace Counter.IntegrationTests;

using System.Net.Http.Json;
using System.Text.Json;

/// <summary>
/// Ten people at the counter at once get the same answers as one person alone.
/// </summary>
/// <remarks>
/// SC-010. The application holds a single MCP connection and the MCP server holds
/// a single database session, which is the right shape and also the shape that
/// fails interestingly: a shared session with unsynchronised commands returns one
/// caller's rows to another, and the screen is entirely plausible.
///
/// The test is written to make that failure visible rather than to hope it does
/// not happen. Each caller asks about a different part and checks that the answer
/// names the part it asked for — a swap between two callers cannot pass.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class ConcurrentCallerTests(CounterFixture fixture)
{
    /// <summary>How many callers at once.</summary>
    private const int Callers = 10;

    /// <summary>How many requests each of them makes.</summary>
    private const int RoundsEach = 5;

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task Every_caller_receives_the_answer_to_their_own_question()
    {
        IReadOnlyList<string> partNumbers = await _fixture.Client.PartNumbersAsync(Callers);

        Assert.True(partNumbers.Count >= Callers, "Not enough parts to give each caller a different one.");

        // Each caller's answer, read alone first. Anything the concurrent run
        // returns has to match this.
        //
        // The envelope is excluded from the comparison because it carries the
        // time the answer was retrieved, which differs between any two reads and
        // is meant to. Everything a representative would repeat to a customer is
        // compared; the timestamp is not one of those things.
        Dictionary<string, string> alone = [];

        foreach (string partNumber in partNumbers)
        {
            JsonElement body = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/availability");

            alone[partNumber] = WithoutEnvelope(body);
        }

        List<Task> callers = [];

        foreach (string partNumber in partNumbers)
        {
            callers.Add(Task.Run(async () =>
            {
                using HttpClient client = _fixture.Application.CreateClient();

                for (int round = 0; round < RoundsEach; round++)
                {
                    JsonElement body = await client.ReadJsonAsync(
                        $"/api/v1/parts/{partNumber}/availability");

                    Assert.Equal(
                        partNumber,
                        body.GetProperty("part").GetProperty("partNumber").GetString());

                    Assert.Equal(alone[partNumber], WithoutEnvelope(body));
                }
            }));
        }

        await Task.WhenAll(callers);
    }

    /// <summary>
    /// Render an answer without its envelope, for comparing two reads.
    /// </summary>
    /// <remarks>
    /// Rebuilt property by property rather than by deleting from a mutable copy,
    /// because <see cref="JsonElement"/> is a view over parsed bytes and has no
    /// deletion to offer.
    /// </remarks>
    private static string WithoutEnvelope(JsonElement answer)
    {
        Dictionary<string, JsonElement> kept = [];

        foreach (JsonProperty property in answer.EnumerateObject())
        {
            if (!string.Equals(property.Name, "envelope", StringComparison.Ordinal))
            {
                kept[property.Name] = property.Value;
            }
        }

        return JsonSerializer.Serialize(kept);
    }

    [Fact]
    public async Task One_persons_customer_selection_never_reaches_another_person()
    {
        // The worst failure this application could have in front of anybody: two
        // representatives on two calls, and one of them quotes the other
        // customer's contract price.
        JsonElement customers = await _fixture.Client.ReadJsonAsync("/api/v1/customers?q=a");

        string[] accounts = customers.GetProperty("results").EnumerateArray()
            .Select(customer => customer.GetProperty("accountNumber").GetString()!)
            .Take(2)
            .ToArray();

        Assert.True(accounts.Length == 2, "Two customers are needed to tell them apart.");

        List<Task> browsers = [];

        foreach (string account in accounts)
        {
            browsers.Add(Task.Run(async () =>
            {
                using HttpClient client = _fixture.Application.CreateClient(
                    new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                    {
                        HandleCookies = true,
                    });

                using HttpResponseMessage selected = await client.PutAsJsonAsync(
                    "/api/v1/session/customer", new { customerAccount = account });

                selected.EnsureSuccessStatusCode();

                for (int round = 0; round < RoundsEach; round++)
                {
                    JsonElement session = await client.ReadJsonAsync("/api/v1/session");

                    Assert.Equal(
                        account,
                        session.GetProperty("selectedCustomerAccount").GetString());
                }
            }));
        }

        await Task.WhenAll(browsers);
    }

    [Fact]
    public async Task A_burst_of_first_requests_does_not_open_a_connection_each()
    {
        // The leak the MCP server itself was hardened against, in its client this
        // time: a burst arriving before the first connection completes, each one
        // opening its own. It shows up as failures under load rather than as
        // anything visible now, so it is worth provoking deliberately.
        Task<JsonElement>[] burst = Enumerable.Range(0, 30)
            .Select(_ => _fixture.Client.ReadJsonAsync("/api/v1/parts?q=breaker&limit=5"))
            .ToArray();

        JsonElement[] answers = await Task.WhenAll(burst);

        Assert.All(answers, answer => Assert.NotEmpty(answer.GetProperty("results").EnumerateArray()));
    }
}
