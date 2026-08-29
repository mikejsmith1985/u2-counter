namespace Counter.IntegrationTests;

using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Search answers fast enough to type into.
/// </summary>
/// <remarks>
/// SC-003. The target is a 95th percentile under one second, and the percentile
/// rather than the mean is the point: a search that is usually instant and
/// occasionally takes four seconds is a search someone stops trusting, and the
/// mean would hide exactly that.
///
/// This runs against the full seeded catalogue through the real MCP server, so
/// what is measured is the path the user is on.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class SearchPerformanceTests(CounterFixture fixture)
{
    /// <summary>How many searches to time.</summary>
    private const int SearchCount = 200;

    /// <summary>The target, in milliseconds.</summary>
    private const int NinetyFifthPercentileBudget = 1000;

    /// <summary>
    /// Terms a person would actually type, in the order they would type them.
    /// </summary>
    /// <remarks>
    /// Growing prefixes rather than random strings: type-ahead fires on each
    /// keystroke, so the query that has to be fast is the two-character one that
    /// matches half the catalogue, not the six-character one that matches four
    /// things.
    /// </remarks>
    private static readonly string[] Terms =
    [
        "b", "br", "bre", "brea", "break", "breake", "breaker",
        "g", "gf", "gfc", "gfci",
        "w", "wi", "wir", "wire",
        "1", "10", "100", "100a",
        "e-", "e-b", "e-br", "s-", "c-w", "a",
    ];

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task The_ninety_fifth_percentile_search_is_under_a_second()
    {
        List<double> timings = new(SearchCount);

        for (int index = 0; index < SearchCount; index++)
        {
            string term = Terms[index % Terms.Length];

            Stopwatch timer = Stopwatch.StartNew();
            using HttpResponseMessage response =
                await _fixture.Client.GetAsync($"/api/v1/parts?q={term}&limit=25");
            _ = await response.Content.ReadAsStringAsync();
            timer.Stop();

            response.EnsureSuccessStatusCode();

            timings.Add(timer.Elapsed.TotalMilliseconds);
        }

        timings.Sort();

        double percentile95 = Percentile(timings, 0.95);
        double median = Percentile(timings, 0.50);

        Assert.True(
            percentile95 < NinetyFifthPercentileBudget,
            $"The 95th percentile search took {percentile95:0}ms " +
            $"(median {median:0}ms, slowest {timings[^1]:0}ms), " +
            $"against a budget of {NinetyFifthPercentileBudget}ms.");
    }

    [Fact]
    public async Task A_broad_search_returns_within_its_limit_rather_than_everything()
    {
        // A single letter matches thousands of parts. Returning them all would be
        // fast to measure and useless to read, and would make the timing above
        // meaningless by moving the cost to the browser.
        JsonElement body = await _fixture.Client.ReadJsonAsync("/api/v1/parts?q=a&limit=25");

        JsonElement results = body.GetProperty("results");

        // Exactly the limit, not merely within it. A single letter matches
        // thousands of parts, so anything less means the search is not finding
        // them -- and `InRange(1, 25)`, which this used to be, was satisfied by
        // a search returning one.
        Assert.Equal(25, results.GetArrayLength());

        // And it says it was capped, which is the other half of the promise.
        Assert.False(body.GetProperty("envelope").GetProperty("isComplete").GetBoolean());
    }

    /// <summary>
    /// Return a percentile from a sorted list, by nearest rank.
    /// </summary>
    /// <remarks>
    /// Nearest rank rather than interpolation: with two hundred samples the two
    /// differ by less than the measurement noise, and a figure that corresponds
    /// to an actual observed request is easier to argue about.
    /// </remarks>
    private static double Percentile(IReadOnlyList<double> sorted, double fraction)
    {
        int rank = (int)Math.Ceiling(fraction * sorted.Count) - 1;

        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
