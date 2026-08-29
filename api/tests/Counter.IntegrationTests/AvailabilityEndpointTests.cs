namespace Counter.IntegrationTests;

using System.Net;
using System.Text.Json;

/// <summary>
/// The availability endpoint, against the real server and the real data.
/// </summary>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class AvailabilityEndpointTests(CounterFixture fixture)
{
    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task A_part_returns_its_branches_and_a_price()
    {
        string partNumber = await _fixture.Client.AnyPartNumberAsync();

        JsonElement body = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/parts/{partNumber}/availability");

        Assert.Equal(partNumber, body.GetProperty("part").GetProperty("partNumber").GetString());
        Assert.True(body.GetProperty("isStockKnown").GetBoolean());
        Assert.NotEmpty(body.GetProperty("branches").EnumerateArray());
        Assert.True(body.GetProperty("pricing").GetProperty("listPrice").GetDecimal() > 0);
    }

    [Fact]
    public async Task Free_to_sell_is_never_more_than_what_is_on_hand()
    {
        // The figure a representative repeats to a customer. If it could exceed
        // on-hand, the screen would promise stock that is not in the building.
        string partNumber = await _fixture.Client.AnyPartNumberAsync();

        JsonElement body = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/parts/{partNumber}/availability");

        foreach (JsonElement branch in body.GetProperty("branches").EnumerateArray())
        {
            int onHand = branch.GetProperty("onHand").GetInt32();
            int freeToSell = branch.GetProperty("freeToSell").GetInt32();

            Assert.InRange(freeToSell, 0, onHand);
        }
    }

    [Fact]
    public async Task The_headline_figure_is_the_branches_added_up_and_nothing_else()
    {
        string partNumber = await _fixture.Client.AnyPartNumberAsync();

        JsonElement body = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/parts/{partNumber}/availability");

        int headline = body.GetProperty("totalFreeToSell").GetInt32();
        int sumOfBranches = body.GetProperty("branches").EnumerateArray()
            .Sum(branch => branch.GetProperty("freeToSell").GetInt32());

        // Stock a supplier has not yet delivered is not stock, so on-order units
        // must not reach this number however plausible that would look.
        Assert.Equal(sumOfBranches, headline);
    }

    [Fact]
    public async Task Unknown_stock_and_no_stock_are_never_the_same_answer()
    {
        // The distinction the whole application exists to preserve. "Nobody has
        // counted this" and "there are none" are different answers, and only one
        // of them is safe to repeat to a customer.
        //
        // Asserted as an invariant over a page of the catalogue rather than
        // against one hand-picked part, so it holds whatever the seeder produced.
        IReadOnlyList<string> partNumbers = await _fixture.Client.PartNumbersAsync(40);

        Assert.NotEmpty(partNumbers);

        foreach (string partNumber in partNumbers)
        {
            JsonElement body = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/availability");

            bool isStockKnown = body.GetProperty("isStockKnown").GetBoolean();
            int branchCount = body.GetProperty("branches").GetArrayLength();

            if (isStockKnown)
            {
                Assert.True(branchCount > 0,
                    $"{partNumber} claims its stock is known while naming no branch.");
            }
            else
            {
                Assert.True(branchCount == 0,
                    $"{partNumber} claims its stock is unknown while naming {branchCount} branches.");
                Assert.Equal(0, body.GetProperty("totalFreeToSell").GetInt32());
            }
        }
    }

    [Fact]
    public async Task An_unknown_part_is_not_found_rather_than_empty()
    {
        using HttpResponseMessage response =
            await _fixture.Client.GetAsync("/api/v1/parts/NOT-A-REAL-PART/availability");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        JsonElement problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("not-found", problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task A_search_with_no_matches_returns_an_empty_list_and_not_an_error()
    {
        // Empty is a legitimate answer to a search and must not arrive looking
        // like a failure — the screen shows a different thing for each.
        JsonElement body = await _fixture.Client.ReadJsonAsync(
            "/api/v1/parts?q=zzzzznothingmatchesthis");

        Assert.Empty(body.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task A_search_with_nothing_to_search_for_is_rejected()
    {
        using HttpResponseMessage response = await _fixture.Client.GetAsync("/api/v1/parts?q=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("gfci")]
    [InlineData("GFCI")]
    [InlineData("  GfCi  ")]
    public async Task Matching_ignores_case_and_surrounding_spacing(string term)
    {
        JsonElement body = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/parts?q={Uri.EscapeDataString(term)}&limit=5");

        Assert.NotEmpty(body.GetProperty("results").EnumerateArray());
    }
}
