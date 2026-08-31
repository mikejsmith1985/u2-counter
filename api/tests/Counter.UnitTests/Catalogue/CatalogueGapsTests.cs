namespace Counter.UnitTests.Catalogue;

using Counter.Domain.Catalogue;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// What the catalogue does when the account does not give it everything.
/// </summary>
/// <remarks>
/// Search is answered from this projection rather than from the ERP, so a part
/// missing here is a part that does not exist as far as anybody using the
/// application is concerned. There is no error and no empty state -- the search
/// simply returns nothing, which reads exactly like a part the branch does not
/// stock.
///
/// A record that cannot be read is an ordinary event in a live account: it was
/// deleted between the selection and the read, or it is one of the handful of
/// broken records every long-lived file accumulates. The right behaviour is to
/// keep every part that did read and say how many did not, and the untested
/// question was whether one unreadable record takes the rest with it.
/// </remarks>
public sealed class CatalogueGapsTests
{
    /// <summary>An account that lists more keys than it will hand over records.</summary>
    /// <param name="keys">Every key the selection returns.</param>
    /// <param name="records">The records that can actually be read.</param>
    private sealed class PartialAccount(
        IReadOnlyList<string> keys,
        Dictionary<string, string> records) : IErpReader
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<string>> SelectKeysAsync(
            string query, int maxKeys, CancellationToken cancellationToken) =>
            Task.FromResult(keys);

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, string>> ReadRecordsAsync(
            string fileName, IReadOnlyList<string> recordIds, CancellationToken cancellationToken)
        {
            Dictionary<string, string> found = [];

            foreach (string id in recordIds)
            {
                if (records.TryGetValue(id, out string? raw))
                {
                    found[id] = raw;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, string>>(found);
        }

        /// <inheritdoc />
        public Task<string> ReadRecordAsync(
            string fileName, string recordId, CancellationToken cancellationToken) =>
            records.TryGetValue(recordId, out string? raw)
                ? Task.FromResult(raw)
                : throw new ErpRecordNotFoundException($"No {recordId}.");

        /// <inheritdoc />
        public Task<IReadOnlyList<DictionaryField>> ListDictionaryAsync(
            string fileName, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DictionaryField>>([]);

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["PRODUCT"]);
    }

    /// <summary>Build a catalogue over an account with a hole in it.</summary>
    /// <param name="keys">Every key the selection returns.</param>
    /// <param name="records">The records that can be read.</param>
    /// <returns>The built projection.</returns>
    private static async Task<CatalogueProjection> BuildAsync(
        IReadOnlyList<string> keys,
        Dictionary<string, string> records)
    {
        CatalogueProjection catalogue = new(
            new PartialAccount(keys, records), NullLogger<CatalogueProjection>.Instance);

        await catalogue.BuildAsync(CancellationToken.None);

        return catalogue;
    }

    /// <summary>Three keys, of which the middle one cannot be read.</summary>
    /// <returns>The keys and the records behind them.</returns>
    private static (IReadOnlyList<string> Keys, Dictionary<string, string> Records) AHoleInTheMiddle() =>
        (
            ["E-BOX00001", "E-BRK00002", "E-WIR00003"],
            new Dictionary<string, string>
            {
                ["E-BOX00001"] = StoredRecords.Product("4in Square Box", "BOX", "3.20"),
                ["E-WIR00003"] = StoredRecords.Product("12/2 Romex", "WIR", "180.00"),
            });

    [Fact]
    public async Task One_unreadable_record_does_not_take_the_rest_of_the_catalogue_with_it()
    {
        // The failure that would matter: a single broken record in a file of
        // three thousand leaving search unable to find any of them.
        (IReadOnlyList<string> keys, Dictionary<string, string> records) = AHoleInTheMiddle();

        CatalogueProjection catalogue = await BuildAsync(keys, records);

        Assert.Equal(2, catalogue.Count);
        Assert.NotNull(catalogue.Find("E-BOX00001"));
        Assert.NotNull(catalogue.Find("E-WIR00003"));
    }

    [Fact]
    public async Task The_part_that_could_not_be_read_is_simply_absent()
    {
        // Not a placeholder and not an entry with empty fields. A part shown
        // with no description and no price would be read as a real part that
        // nobody had filled in.
        (IReadOnlyList<string> keys, Dictionary<string, string> records) = AHoleInTheMiddle();

        CatalogueProjection catalogue = await BuildAsync(keys, records);

        Assert.Null(catalogue.Find("E-BRK00002"));
    }

    [Fact]
    public async Task Searching_still_finds_the_parts_that_did_read()
    {
        // Search is answered from here rather than from the account, so this is
        // the behaviour a person actually meets.
        (IReadOnlyList<string> keys, Dictionary<string, string> records) = AHoleInTheMiddle();

        CatalogueProjection catalogue = await BuildAsync(keys, records);

        Assert.NotEmpty(catalogue.Search("Romex", 10));
    }

    [Fact]
    public async Task An_empty_account_builds_an_empty_catalogue_rather_than_failing()
    {
        // A file with nothing in it is a legitimate state, and an exception here
        // would stop the application starting at all.
        CatalogueProjection catalogue = await BuildAsync([], []);

        Assert.Equal(0, catalogue.Count);
        Assert.Empty(catalogue.Browse(10));
    }

    [Fact]
    public async Task An_account_that_hands_back_nothing_at_all_is_an_empty_catalogue()
    {
        // Keys selected and no records readable. The worst case, and it must
        // still leave a working application rather than a half-built one.
        CatalogueProjection catalogue = await BuildAsync(["E-BOX00001", "E-BRK00002"], []);

        Assert.Equal(0, catalogue.Count);
        Assert.Null(catalogue.Find("E-BOX00001"));
    }

    [Fact]
    public async Task Parts_are_held_in_key_order_however_the_account_returned_them()
    {
        // Browse shows the first few parts on an empty screen, so an order that
        // changed between builds would make the same deployment look different
        // on every cold start.
        CatalogueProjection catalogue = await BuildAsync(
            ["E-WIR00003", "E-BOX00001"],
            new Dictionary<string, string>
            {
                ["E-WIR00003"] = StoredRecords.Product("12/2 Romex", "WIR", "180.00"),
                ["E-BOX00001"] = StoredRecords.Product("4in Square Box", "BOX", "3.20"),
            });

        Assert.Equal(
            ["E-BOX00001", "E-WIR00003"],
            catalogue.Browse(10).Select(part => part.PartNumber));
    }
}
