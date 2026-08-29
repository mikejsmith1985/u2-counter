namespace Counter.IntegrationTests;

using System.Text.Json;

/// <summary>
/// Seeing what is in the database without first guessing a name.
/// </summary>
/// <remarks>
/// Found by using the application rather than by testing it. Opening it cold,
/// there is a search box for parts and a search box for customers, and no way to
/// answer the question anyone actually arrives with: what is in here?
///
/// A counter representative learns their catalogue over months and can type a
/// part number from memory. Nobody meeting the system for the first time can,
/// and that includes everyone this is being shown to. A search box facing a
/// stranger is a locked door with no handle.
///
/// Browsing is deliberately a separate route from searching rather than a search
/// with the term left out. The two answer different questions -- "what is here"
/// and "where is this" -- and they return different things: a browse says how
/// many there are in total, which a search has no business claiming. Keeping them
/// apart also leaves the search guard intact, and that guard exists because a
/// query of punctuation once returned the whole catalogue ranked as though every
/// row were a strong match.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class BrowseTests(CounterFixture fixture)
{
    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task Parts_can_be_browsed_without_a_search_term()
    {
        JsonElement browsed = await _fixture.Client.ReadJsonAsync("/api/v1/parts/browse");

        Assert.NotEmpty(browsed.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task Browsing_parts_says_how_many_there_are_altogether()
    {
        // The number is the answer to "what is in here". A page of twenty parts
        // with no total tells a reader they are looking at everything, which for
        // a three thousand part catalogue is wrong by two orders of magnitude.
        JsonElement browsed = await _fixture.Client.ReadJsonAsync("/api/v1/parts/browse");

        int total = browsed.GetProperty("totalCount").GetInt32();
        int shown = browsed.GetProperty("results").GetArrayLength();

        Assert.True(total > shown, $"Showed {shown} of a claimed {total}.");
    }

    [Fact]
    public async Task Browsing_parts_honours_a_limit()
    {
        JsonElement browsed =
            await _fixture.Client.ReadJsonAsync("/api/v1/parts/browse?limit=5");

        Assert.Equal(5, browsed.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Browsing_parts_returns_them_in_a_stable_order()
    {
        // Two identical requests must agree. A picker whose contents reshuffle
        // between openings is one nobody can learn.
        JsonElement first = await _fixture.Client.ReadJsonAsync("/api/v1/parts/browse?limit=10");
        JsonElement again = await _fixture.Client.ReadJsonAsync("/api/v1/parts/browse?limit=10");

        Assert.Equal(PartNumbers(first), PartNumbers(again));
    }

    [Fact]
    public async Task Customers_can_be_browsed_without_a_search_term()
    {
        JsonElement browsed = await _fixture.Client.ReadJsonAsync("/api/v1/customers/browse");

        Assert.NotEmpty(browsed.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task Browsing_customers_says_how_many_there_are_altogether()
    {
        JsonElement browsed = await _fixture.Client.ReadJsonAsync("/api/v1/customers/browse");

        int total = browsed.GetProperty("totalCount").GetInt32();

        Assert.True(total > 0, "The directory reported no customers at all.");
    }

    [Fact]
    public async Task A_browsed_customer_carries_what_the_picker_shows()
    {
        // Account number, name and price class. A row that cannot show the price
        // class makes the representative pick a customer to find out what
        // choosing them would do.
        JsonElement browsed = await _fixture.Client.ReadJsonAsync("/api/v1/customers/browse");
        JsonElement first = browsed.GetProperty("results").EnumerateArray().First();

        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("accountNumber").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("name").GetString()));
        Assert.True(first.TryGetProperty("priceClass", out _));
    }

    [Fact]
    public async Task Searching_still_refuses_an_empty_term()
    {
        // The guard that browsing must not weaken. A search with nothing in it is
        // still a bad request; a browse is a different question asked at a
        // different address.
        using HttpResponseMessage response =
            await _fixture.Client.GetAsync("/api/v1/parts?q=");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Searching_for_punctuation_still_matches_nothing()
    {
        JsonElement searched = await _fixture.Client.ReadJsonAsync("/api/v1/parts?q=*");

        Assert.Empty(searched.GetProperty("results").EnumerateArray());
    }

    /// <summary>The part numbers in a search or browse response, in order.</summary>
    private static string[] PartNumbers(JsonElement response) =>
        response.GetProperty("results")
            .EnumerateArray()
            .Select(part => part.GetProperty("partNumber").GetString()!)
            .ToArray();
}
