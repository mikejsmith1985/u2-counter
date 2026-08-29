namespace Counter.IntegrationTests;

using System.Text.Json;

/// <summary>
/// An answer that was cut short says so.
/// </summary>
/// <remarks>
/// This suite exists because the flag it checks was, for the whole life of the
/// project until now, incapable of being false. Every response was built with
/// `ResponseEnvelope.Complete()`; `Partial` was written, documented, rendered by
/// the front end, and never called. The contract said `is_complete` must never be
/// assumed true, and the server could produce nothing else.
///
/// That is the failure this application is entirely about, turned on itself: a
/// partial answer presented as a whole one. A representative reading fifteen
/// customers has no way to know theirs was the sixteenth, and quotes a price
/// against the wrong contract.
///
/// So each cap is provoked here, and each has to admit it.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class PartialAnswerTests(CounterFixture fixture)
{
    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task A_customer_search_that_hits_its_cap_says_so()
    {
        // "a" matches most of a hundred and fifty seeded customers, and the
        // endpoint shows fifteen.
        JsonElement body = await _fixture.Client.ReadJsonAsync("/api/v1/customers?q=al");

        JsonElement envelope = body.GetProperty("envelope");

        Assert.False(
            envelope.GetProperty("isComplete").GetBoolean(),
            "A capped customer search reported itself complete. Somebody whose account " +
            "was the sixteenth match is invisible, and the price quoted is another " +
            "customer's.");

        Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("warning").GetString()));
    }

    [Fact]
    public async Task A_customer_search_that_fits_reports_itself_complete()
    {
        // The other half. A flag that is always false is as useless as one that
        // is always true, and rather more alarming.
        JsonElement body = await _fixture.Client.ReadJsonAsync(
            "/api/v1/customers?q=zzzznosuchcustomer");

        JsonElement envelope = body.GetProperty("envelope");

        Assert.True(envelope.GetProperty("isComplete").GetBoolean());
        Assert.Null(envelope.GetProperty("warning").GetString());
    }

    [Fact]
    public async Task A_part_search_filled_to_its_limit_says_it_may_be_capped()
    {
        // Asked for five against a term matching hundreds.
        JsonElement body = await _fixture.Client.ReadJsonAsync("/api/v1/parts?q=a&limit=5");

        Assert.Equal(5, body.GetProperty("results").GetArrayLength());

        JsonElement envelope = body.GetProperty("envelope");

        Assert.False(envelope.GetProperty("isComplete").GetBoolean());
        Assert.Contains("5", envelope.GetProperty("warning").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_part_search_that_returns_fewer_than_asked_for_is_complete()
    {
        JsonElement body = await _fixture.Client.ReadJsonAsync(
            "/api/v1/parts?q=zzzznothinglikethis&limit=25");

        Assert.Empty(body.GetProperty("results").EnumerateArray());
        Assert.True(body.GetProperty("envelope").GetProperty("isComplete").GetBoolean());
    }

    [Fact]
    public async Task The_warning_is_something_a_person_can_act_on()
    {
        // A warning that says an answer is incomplete and not what to do about it
        // leaves the reader stuck with a number they have been told not to trust.
        JsonElement body = await _fixture.Client.ReadJsonAsync("/api/v1/parts?q=a&limit=5");

        string warning = body.GetProperty("envelope").GetProperty("warning").GetString()!;

        Assert.Contains("narrow", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_envelope_carries_the_demonstration_flag_whether_complete_or_not()
    {
        // The partial path is newer and easier to get wrong. A capped answer with
        // no demonstration badge is exactly the screenshot that gets mistaken for
        // production data.
        foreach (string path in new[]
        {
            "/api/v1/parts?q=a&limit=5",
            "/api/v1/parts?q=zzzznothinglikethis",
            "/api/v1/customers?q=al",
        })
        {
            JsonElement body = await _fixture.Client.ReadJsonAsync(path);

            Assert.True(
                body.GetProperty("envelope").GetProperty("isDemonstrationData").GetBoolean(),
                $"{path} returned figures without saying they are demonstration data.");
        }
    }
}
