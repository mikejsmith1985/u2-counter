namespace Counter.IntegrationTests;

using System.Net;
using System.Text.Json;

/// <summary>
/// What is listed as holding stock adds up to what is recorded as committed.
/// </summary>
/// <remarks>
/// SC-014. The arithmetic is the whole feature: a representative looking at
/// "40 committed" needs to see which orders account for it, and the moment the
/// listed orders quietly fail to add up the screen has started guessing.
///
/// The unaccounted figure exists precisely so the sum always closes. An order
/// closed without releasing its allocation leaves stock committed to nothing, and
/// the honest screen shows that as a row rather than absorbing it.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class CommitmentReconciliationTests(CounterFixture fixture)
{
    /// <summary>The order states that may hold stock.</summary>
    /// <remarks>
    /// A quotation has not been placed and a shipped order has left the building.
    /// Either one counted here would overstate what is held.
    /// </remarks>
    private static readonly string[] HoldingStates = ["Confirmed", "Allocated", "Picking"];

    /// <summary>How many parts to sweep.</summary>
    private const int PartsToCheck = 25;

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task Listed_commitments_plus_unaccounted_equal_the_committed_total()
    {
        int branchesChecked = 0;

        foreach (string partNumber in await _fixture.Client.PartNumbersAsync(PartsToCheck))
        {
            JsonElement availability = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/availability");

            foreach (JsonElement branch in availability.GetProperty("branches").EnumerateArray())
            {
                string branchCode = branch.GetProperty("branchCode").GetString()!;

                using HttpResponseMessage response = await _fixture.Client.GetAsync(
                    $"/api/v1/parts/{partNumber}/commitments?branchCode={branchCode}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                response.EnsureSuccessStatusCode();

                JsonElement held = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync()).RootElement;

                int committedTotal = held.GetProperty("committedTotal").GetInt32();
                int accountedFor = held.GetProperty("accountedFor").GetInt32();
                int unaccounted = held.GetProperty("unaccounted").GetInt32();

                int listed = held.GetProperty("commitments").EnumerateArray()
                    .Sum(commitment => commitment.GetProperty("quantity").GetInt32());

                string where = $"{partNumber} at {branchCode}";

                Assert.True(
                    accountedFor == listed,
                    $"{where}: accountedFor is {accountedFor} but the listed orders total {listed}.");

                Assert.True(
                    accountedFor + unaccounted == committedTotal,
                    $"{where}: {accountedFor} accounted plus {unaccounted} unaccounted " +
                    $"does not equal the committed total of {committedTotal}.");

                // The committed figure must match the one the availability screen
                // showed for the same branch, or the two screens disagree about
                // the same record.
                Assert.Equal(branch.GetProperty("committed").GetInt32(), committedTotal);

                branchesChecked++;
            }
        }

        Assert.True(branchesChecked > 0, "No branch was reconciled.");
    }

    [Fact]
    public async Task Only_orders_that_can_hold_stock_are_listed()
    {
        // FR-018. A quotation nobody placed and an order already shipped both
        // look like demand; neither is holding anything on a shelf.
        foreach (string partNumber in await _fixture.Client.PartNumbersAsync(PartsToCheck))
        {
            JsonElement availability = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/availability");

            foreach (JsonElement branch in availability.GetProperty("branches").EnumerateArray())
            {
                string branchCode = branch.GetProperty("branchCode").GetString()!;

                using HttpResponseMessage response = await _fixture.Client.GetAsync(
                    $"/api/v1/parts/{partNumber}/commitments?branchCode={branchCode}");

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                JsonElement held = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync()).RootElement;

                foreach (JsonElement commitment in held.GetProperty("commitments").EnumerateArray())
                {
                    string state = commitment.GetProperty("state").GetString()!;

                    Assert.Contains(state, HoldingStates);

                    // A commitment of zero would be a row that explains nothing
                    // while making the list look longer.
                    Assert.True(commitment.GetProperty("quantity").GetInt32() > 0);
                }
            }
        }
    }

    [Fact]
    public async Task A_branch_the_part_holds_no_position_at_is_not_found()
    {
        string partNumber = await _fixture.Client.AnyPartNumberAsync();

        using HttpResponseMessage response = await _fixture.Client.GetAsync(
            $"/api/v1/parts/{partNumber}/commitments?branchCode=NOSUCH");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_branch_is_rejected_rather_than_guessed_at()
    {
        string partNumber = await _fixture.Client.AnyPartNumberAsync();

        using HttpResponseMessage response = await _fixture.Client.GetAsync(
            $"/api/v1/parts/{partNumber}/commitments?branchCode=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
