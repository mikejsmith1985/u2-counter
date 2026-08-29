namespace Counter.IntegrationTests;

using System.Net;
using System.Text.Json;
using Counter.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Every failure arrives typed, and says something a person could read aloud.
/// </summary>
/// <remarks>
/// SC-009. The client switches on the type rather than guessing from an empty
/// body, and the reason is the one this application is built around: an empty
/// result and an unreachable system look identical if both arrive as nothing.
///
/// The detail is asserted to be a sentence rather than merely present, because
/// somebody on a call is going to repeat it to the customer waiting, and
/// "Object reference not set to an instance of an object" is not a thing anyone
/// can say out loud.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class FailureShapeTests(CounterFixture fixture)
{
    /// <summary>Shortest detail that could plausibly be a sentence.</summary>
    private const int ShortestUsefulDetail = 10;

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task An_unknown_part_returns_a_typed_not_found()
    {
        await AssertProblemAsync(
            "/api/v1/parts/NO-SUCH-PART-0001/availability",
            HttpStatusCode.NotFound,
            ErpProblemFilter.NotFoundType);
    }

    [Fact]
    public async Task An_unknown_part_record_returns_a_typed_not_found()
    {
        await AssertProblemAsync(
            "/api/v1/parts/NO-SUCH-PART-0001/record",
            HttpStatusCode.NotFound,
            ErpProblemFilter.NotFoundType);
    }

    [Fact]
    public async Task A_search_with_no_term_returns_a_typed_bad_request()
    {
        await AssertProblemAsync(
            "/api/v1/parts?q=",
            HttpStatusCode.BadRequest,
            "invalid-request");
    }

    [Fact]
    public async Task An_unreachable_erp_is_reported_as_unreachable_and_never_as_empty()
    {
        // The one that would do real damage. A representative reading an empty
        // result would tell a customer there is no stock; a representative
        // reading "the system could not be reached" would ring them back.
        using HttpClient client = _fixture.Application.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // A port nothing answers on. Refusing is the fast version of
                    // the same failure a dead ERP produces.
                    ["Erp:Endpoint"] = "http://127.0.0.1:1/",
                    ["Erp:RequestBudget"] = "00:00:02",
                }))).CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/v1/parts/S-BRK00000/availability");

        Assert.False(response.IsSuccessStatusCode,
            "An unreachable ERP returned a success. This is the failure the whole design exists to prevent.");

        string body = await response.Content.ReadAsStringAsync();
        JsonElement problem = JsonDocument.Parse(body).RootElement;

        Assert.Equal(
            ErpProblemFilter.UnreachableType,
            problem.GetProperty("type").GetString());

        Assert.Equal(StatusCodes.Status504GatewayTimeout, (int)response.StatusCode);
        AssertReadableDetail(problem);
    }

    [Fact]
    public async Task Every_problem_names_the_path_it_came_from()
    {
        // A trail of problems that all look alike is a trail nobody can follow
        // back to the request that produced one.
        using HttpResponseMessage response =
            await _fixture.Client.GetAsync("/api/v1/parts/NO-SUCH-PART-0002/availability");

        JsonElement problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(
            "/api/v1/parts/NO-SUCH-PART-0002/availability",
            problem.GetProperty("instance").GetString());
    }

    /// <summary>Assert a request fails with the expected status and type.</summary>
    private async Task AssertProblemAsync(string path, HttpStatusCode status, string type)
    {
        using HttpResponseMessage response = await _fixture.Client.GetAsync(path);

        Assert.Equal(status, response.StatusCode);

        JsonElement problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(type, problem.GetProperty("type").GetString());
        AssertReadableDetail(problem);
    }

    /// <summary>Assert the detail is something a person could say out loud.</summary>
    private static void AssertReadableDetail(JsonElement problem)
    {
        string detail = problem.GetProperty("detail").GetString() ?? string.Empty;

        Assert.True(
            detail.Length >= ShortestUsefulDetail,
            $"The detail '{detail}' is too short to tell anyone anything.");

        // A stack frame or an exception type name has leaked if these appear.
        Assert.DoesNotContain("Exception", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", detail, StringComparison.Ordinal);
    }
}
