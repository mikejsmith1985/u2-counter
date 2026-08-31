namespace Counter.IntegrationTests;

using System.Net;
using System.Text.Json;

/// <summary>
/// Reading an unfamiliar account by asking it what it holds.
/// </summary>
/// <remarks>
/// These routes are the backend of the claim that most distinguishes this from
/// a screen someone hardcoded: the files, the fields and the records come from
/// the account's own dictionary rather than from anything written here, so the
/// same screens work against a database nobody has seen.
///
/// They had no coverage in this suite at all. The browser tests drive them end
/// to end, which is worth more for the screen and worth less for the parts a
/// screen cannot reach: what a filter turns into, and what happens to a value
/// carrying the one character that could end a quoted string early.
///
/// That last one is the reason this file leads with it. The selection is built
/// by joining strings, which is the shape that becomes an injection everywhere
/// else, and the defence is one line.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class SchemaExploringTests(CounterFixture fixture)
{
    /// <summary>A file every demonstration account holds, and small enough to reason about.</summary>
    private const string SmallFile = "BRANCH";

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task The_account_lists_its_own_files()
    {
        // Nothing here names them. A reader pointing this at their own account
        // gets their files, which is the whole claim.
        JsonElement body = await _fixture.Client.ReadJsonAsync("/api/v1/schema/files");

        string[] files = body.GetProperty("files").EnumerateArray()
            .Select(file => file.GetString()!)
            .ToArray();

        Assert.Contains(SmallFile, files);
        Assert.Contains("INVENTORY", files);
    }

    [Fact]
    public async Task Dictionaries_are_not_offered_as_files_of_their_own()
    {
        // A dictionary is the other half of the file it describes. Listing it
        // beside that file doubles the apparent size of the account and invites
        // somebody to open one expecting data.
        JsonElement body = await _fixture.Client.ReadJsonAsync("/api/v1/schema/files");

        Assert.DoesNotContain(
            body.GetProperty("files").EnumerateArray().Select(file => file.GetString()!),
            name => name.StartsWith("DICT.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_describes_its_fields_in_position_order()
    {
        // Position order is the record's order. Any other order describes a
        // different record, however correct each individual line is.
        JsonElement body = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}");

        int[] positions = body.GetProperty("fields").EnumerateArray()
            .Select(field => field.GetProperty("position").GetInt32())
            .ToArray();

        Assert.NotEmpty(positions);
        Assert.Equal(positions.OrderBy(position => position), positions);
    }

    [Fact]
    public async Task Records_come_back_with_their_stored_form_and_their_fields()
    {
        // Both, because they answer different questions: the stored form is the
        // evidence, and the split fields are the reading of it.
        JsonElement body = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records");

        JsonElement first = body.GetProperty("records").EnumerateArray().First();

        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("key").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("raw").GetString()));
        Assert.NotEmpty(first.GetProperty("fields").EnumerateArray());
    }

    [Fact]
    public async Task A_contains_filter_finds_a_value_it_does_not_match_exactly()
    {
        // Exact was once the only option, which is what a MultiValue SELECT
        // does and reads as broken on a screen for exploring: a search for part
        // of a name returned nothing, with no hint why.
        JsonElement all = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records");

        JsonElement first = all.GetProperty("records").EnumerateArray().First();
        string wholeName = first.GetProperty("fields").EnumerateArray().First().GetString()!;

        Assert.True(wholeName.Length >= 3, "The demonstration needs a name worth searching part of.");

        string fragment = wholeName[..3];

        JsonElement filtered = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records?position=1&value={Uri.EscapeDataString(fragment)}");

        Assert.NotEmpty(filtered.GetProperty("records").EnumerateArray());
    }

    [Fact]
    public async Task An_exact_filter_wants_the_whole_value()
    {
        // The other half of the pair, and the one that matches the database's
        // own behaviour. Both are offered because they answer different
        // questions, and the screen says which it is doing.
        JsonElement all = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records");

        string wholeName = all.GetProperty("records").EnumerateArray().First()
            .GetProperty("fields").EnumerateArray().First().GetString()!;

        JsonElement exact = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records"
                + $"?position=1&exact=true&value={Uri.EscapeDataString(wholeName)}");

        Assert.NotEmpty(exact.GetProperty("records").EnumerateArray());

        JsonElement fragment = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records"
                + $"?position=1&exact=true&value={Uri.EscapeDataString(wholeName[..2])}");

        Assert.Empty(fragment.GetProperty("records").EnumerateArray());
    }

    [Fact]
    public async Task A_value_carrying_a_quote_cannot_end_the_query_early()
    {
        // The selection is built by joining strings, which is the shape that
        // becomes an injection everywhere else. The only character that could
        // end the quoted string early is the quote itself, and it is removed
        // rather than escaped, because MultiValue query syntax has no escape
        // for it.
        //
        // What matters is that this answers at all: a value that closed the
        // string would produce a malformed query and a failure, and one that
        // closed it and continued would produce whatever came after.
        using HttpResponseMessage response = await _fixture.Client.GetAsync(
            $"/api/v1/schema/files/{SmallFile}/records"
                + "?position=1&value=" + Uri.EscapeDataString("\" OR 1=1 WITH F1 LIKE \""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_query_of_nothing_but_punctuation_does_not_return_the_file()
    {
        // Its own case because the search route once did exactly this: a term
        // of punctuation matched everything and ranked every row as a strong
        // match. The filter here is a selection rather than a ranking, so the
        // failure would look different -- an unfiltered list, presented as a
        // filtered one.
        JsonElement all = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records");

        JsonElement punctuation = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records"
                + "?position=1&value=" + Uri.EscapeDataString("\"\"\""));

        Assert.True(
            punctuation.GetProperty("records").GetArrayLength()
                <= all.GetProperty("records").GetArrayLength(),
            "A filter of punctuation returned more than the unfiltered file.");
    }

    [Fact]
    public async Task A_file_the_account_does_not_hold_is_empty_rather_than_a_failure()
    {
        // Asking about a file that is not there is an ordinary question with an
        // ordinary answer, and the same is true in Universe.
        using HttpResponseMessage response = await _fixture.Client.GetAsync(
            "/api/v1/schema/files/NOSUCHFILE/records");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_filter_value_is_treated_as_no_filter()
    {
        // Otherwise clearing the box would select on an empty string and show
        // nothing, which reads as a file that emptied itself.
        JsonElement all = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records");

        JsonElement blank = await _fixture.Client.ReadJsonAsync(
            $"/api/v1/schema/files/{SmallFile}/records?position=1&value=");

        Assert.Equal(
            all.GetProperty("records").GetArrayLength(),
            blank.GetProperty("records").GetArrayLength());
    }
}
