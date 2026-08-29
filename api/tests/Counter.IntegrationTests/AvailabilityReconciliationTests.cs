namespace Counter.IntegrationTests;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Every figure on the screen, checked against the record it came from.
/// </summary>
/// <remarks>
/// SC-005. This is the test that would catch the failure that actually matters:
/// a parser that reads position three of one field and position four of another
/// produces a screen that is entirely plausible and entirely wrong — Aurora's
/// on-hand beside Pueblo's committed, with nothing to indicate it.
///
/// So the raw record is fetched alongside the parsed answer and split here, in
/// the test, by the marks themselves. The comparison is only worth anything
/// because the two sides are read by different code: reusing the parser to check
/// the parser would agree with itself no matter what it did.
/// </remarks>
/// <param name="fixture">The started services.</param>
[Collection(CounterCollection.Name)]
public sealed class AvailabilityReconciliationTests(CounterFixture fixture)
{
    /// <summary>Separates fields within a record.</summary>
    private const char AttributeMark = (char)254;

    /// <summary>Separates values within a field.</summary>
    private const char ValueMark = (char)253;

    /// <summary>
    /// Where each field lands after splitting on the attribute mark.
    /// </summary>
    /// <remarks>
    /// MultiValue numbers fields from one and the contract describes them that
    /// way, but a split produces a zero-based array. These are the array
    /// positions, one lower than the field numbers in the file contract, and the
    /// off-by-one between the two is exactly the mistake this suite exists to
    /// catch -- so it is written down rather than counted on the fly.
    /// </remarks>
    private const int BranchField = 0;
    private const int OnHandField = 1;
    private const int CommittedField = 2;
    private const int OnOrderField = 3;

    /// <summary>How many parts to reconcile.</summary>
    /// <remarks>
    /// Every part would take minutes and prove the same thing. Forty covers each
    /// branch many times over while keeping the suite something a person will run
    /// before pushing rather than after being asked to.
    /// </remarks>
    private const int PartsToCheck = 40;

    private readonly CounterFixture _fixture = fixture;

    [Fact]
    public async Task Every_parsed_branch_position_matches_the_record_it_came_from()
    {
        IReadOnlyList<string> partNumbers = await _fixture.Client.PartNumbersAsync(PartsToCheck);

        Assert.NotEmpty(partNumbers);

        int reconciled = 0;

        foreach (string partNumber in partNumbers)
        {
            JsonElement availability = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/availability");

            if (!availability.GetProperty("isStockKnown").GetBoolean())
            {
                continue;
            }

            JsonElement stored = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/record");

            string raw = stored.GetProperty("rawRecord").GetString()!;
            string[] fields = raw.Split(AttributeMark);

            string[] branches = ValuesOf(fields, BranchField);
            string[] onHand = ValuesOf(fields, OnHandField);
            string[] committed = ValuesOf(fields, CommittedField);
            string[] onOrder = ValuesOf(fields, OnOrderField);

            JsonElement[] parsed = availability.GetProperty("branches").EnumerateArray().ToArray();

            Assert.Equal(branches.Length, parsed.Length);

            for (int position = 0; position < branches.Length; position++)
            {
                JsonElement row = parsed[position];
                string where = $"{partNumber} position {position + 1}";

                // The branch code has to match at the same position, or every
                // figure below it belongs to a different branch.
                Assert.Equal(branches[position], row.GetProperty("branchCode").GetString());

                Assert.Equal(Number(onHand, position), row.GetProperty("onHand").GetInt32());
                Assert.Equal(Number(committed, position), row.GetProperty("committed").GetInt32());
                Assert.Equal(Number(onOrder, position), row.GetProperty("onOrder").GetInt32());

                int expectedFree = Math.Max(0, Number(onHand, position) - Number(committed, position));
                Assert.True(
                    expectedFree == row.GetProperty("freeToSell").GetInt32(),
                    $"{where}: free to sell should be {expectedFree}.");
            }

            reconciled++;
        }

        Assert.True(reconciled > 0, "No part with a stock record was reconciled.");
    }

    [Fact]
    public async Task The_headline_figure_matches_the_record_rather_than_the_screen()
    {
        // Deliberately computed from the raw record rather than from the branch
        // rows. Summing the rows would agree with the screen even if both were
        // built from the wrong fields.
        IReadOnlyList<string> partNumbers = await _fixture.Client.PartNumbersAsync(PartsToCheck);

        foreach (string partNumber in partNumbers)
        {
            JsonElement availability = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/availability");

            if (!availability.GetProperty("isStockKnown").GetBoolean())
            {
                continue;
            }

            JsonElement stored = await _fixture.Client.ReadJsonAsync(
                $"/api/v1/parts/{partNumber}/record");

            string[] fields = stored.GetProperty("rawRecord").GetString()!.Split(AttributeMark);
            string[] onHand = ValuesOf(fields, OnHandField);
            string[] committed = ValuesOf(fields, CommittedField);

            int expected = 0;

            for (int position = 0; position < onHand.Length; position++)
            {
                expected += Math.Max(0, Number(onHand, position) - Number(committed, position));
            }

            Assert.Equal(expected, availability.GetProperty("totalFreeToSell").GetInt32());
        }
    }

    /// <summary>Split one field of a record into its values.</summary>
    /// <remarks>
    /// A field shorter than the branch list is padded by the parser rather than
    /// treated as an error, because an unrecorded figure is a zero and not a
    /// reason to refuse the whole record. An absent field returns nothing, which
    /// <see cref="Number"/> reads as zero for the same reason.
    /// </remarks>
    private static string[] ValuesOf(string[] fields, int fieldNumber) =>
        fieldNumber < fields.Length ? fields[fieldNumber].Split(ValueMark) : [];

    /// <summary>Read one value as a number, treating an absent one as zero.</summary>
    private static int Number(string[] values, int position) =>
        position < values.Length &&
        int.TryParse(values[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
}
