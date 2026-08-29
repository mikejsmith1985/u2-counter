namespace Counter.IntegrationTests;

using System.Text.Json;

/// <summary>
/// Reading the API in tests, with failures that say what went wrong.
/// </summary>
/// <remarks>
/// A test that fails on `response.IsSuccessStatusCode` tells whoever reads the
/// run that something returned a bad status, and nothing else. These helpers put
/// the path and the body into the failure, because the body is where the reason
/// is and re-running by hand to find it wastes the time the suite was meant to
/// save.
/// </remarks>
public static class ApiReader
{
    /// <summary>
    /// Read a successful response as JSON.
    /// </summary>
    /// <param name="client">The client to read through.</param>
    /// <param name="path">What to ask for.</param>
    /// <returns>The parsed body.</returns>
    public static async Task<JsonElement> ReadJsonAsync(this HttpClient client, string path)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.GetAsync(path);
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET {path} returned {(int)response.StatusCode}: {Shorten(body)}");

        // Cloned: the document is disposed when this method returns, and an
        // element that outlives its document throws where it is read rather than
        // where the mistake was made.
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Return a part number the seeded catalogue holds.</summary>
    /// <param name="client">The client to read through.</param>
    /// <param name="term">What to search for.</param>
    public static async Task<string> AnyPartNumberAsync(this HttpClient client, string term = "breaker")
    {
        JsonElement body = await client.ReadJsonAsync($"/api/v1/parts?q={term}&limit=1");

        JsonElement.ArrayEnumerator results = body.GetProperty("results").EnumerateArray();

        Assert.True(results.Any(), $"The catalogue returned no match for '{term}'.");

        return body.GetProperty("results").EnumerateArray().First()
            .GetProperty("partNumber").GetString()!;
    }

    /// <summary>Return several part numbers, for tests that sweep the data.</summary>
    /// <param name="client">The client to read through.</param>
    /// <param name="count">How many to return.</param>
    public static async Task<IReadOnlyList<string>> PartNumbersAsync(this HttpClient client, int count)
    {
        // A single letter matches broadly, which is what a sweep wants: a
        // narrower term would quietly test one manufacturer's records.
        JsonElement body = await client.ReadJsonAsync($"/api/v1/parts?q=a&limit={count}");

        return body.GetProperty("results").EnumerateArray()
            .Select(result => result.GetProperty("partNumber").GetString()!)
            .ToList();
    }

    /// <summary>Trim a body to something a failure message can carry.</summary>
    private static string Shorten(string body) =>
        body.Length <= 500 ? body : body[..500] + "…";
}
