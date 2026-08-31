namespace Counter.UnitTests;

using Counter.Domain.Catalogue;
using Counter.Infrastructure.Mcp;

/// <summary>
/// An ERP that holds records in memory, in the form the real one stores them.
/// </summary>
/// <remarks>
/// Records go in as marked strings rather than as objects, so a test that uses
/// this exercises the parsing as well as the logic above it. A fake that handed
/// back ready-made domain objects would step over the part most likely to be
/// wrong: reading a MultiValue record is where a quantity ends up against the
/// wrong branch.
/// </remarks>
/// <param name="records">Keyed by file name, then by record id.</param>
public sealed class InMemoryErp(Dictionary<string, Dictionary<string, string>> records)
    : IErpReader
{
    /// <summary>How many times a record has been read, for testing a cache.</summary>
    public int RecordReads { get; private set; }

    /// <inheritdoc />
    public Task<string> ReadRecordAsync(
        string fileName,
        string recordId,
        CancellationToken cancellationToken)
    {
        RecordReads++;

        if (records.TryGetValue(fileName, out Dictionary<string, string>? file) &&
            file.TryGetValue(recordId, out string? raw))
        {
            return Task.FromResult(raw);
        }

        throw new ErpRecordNotFoundException($"No record {recordId} in {fileName}.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// The real query language is not reimplemented here. A query names its file,
    /// so the file name is taken from it and every key returned — which is what
    /// the callers under test do with it.
    /// </remarks>
    public Task<IReadOnlyList<string>> SelectKeysAsync(
        string query,
        int maxKeys,
        CancellationToken cancellationToken)
    {
        string file = records.Keys.FirstOrDefault(query.Contains, string.Empty);

        IReadOnlyList<string> keys = file.Length == 0
            ? []
            : [.. records[file].Keys.Take(maxKeys)];

        return Task.FromResult(keys);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadRecordsAsync(
        string fileName,
        IReadOnlyList<string> recordIds,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> found = [];

        foreach (string id in recordIds)
        {
            RecordReads++;

            if (records.TryGetValue(fileName, out Dictionary<string, string>? file) &&
                file.TryGetValue(id, out string? raw))
            {
                found[id] = raw;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(found);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DictionaryField>> ListDictionaryAsync(
        string fileName,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DictionaryField>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([.. records.Keys]);
}

/// <summary>
/// Records in their stored form, built the way the demonstration data holds them.
/// </summary>
public static class StoredRecords
{
    /// <summary>Separates fields.</summary>
    public const char AttributeMark = (char)254;

    /// <summary>Separates values within a field.</summary>
    public const char ValueMark = (char)253;

    /// <summary>A customer: name, address, contacts, phones, terms, class, branch.</summary>
    /// <param name="name">Trading name.</param>
    /// <param name="priceClass">Which price class they buy on.</param>
    /// <returns>The record as stored.</returns>
    public static string Customer(string name, string priceClass) =>
        string.Join(
            AttributeMark,
            name, "1 Wazee St", "A Contact", "303-555-0100", "NET30", priceClass, "DEN");

    /// <summary>A product: description, maker, their number, unit, category, price, status.</summary>
    /// <param name="description">What the part is.</param>
    /// <param name="category">The category joining to contract pricing.</param>
    /// <param name="listPrice">The undiscounted price.</param>
    /// <returns>The record as stored.</returns>
    public static string Product(string description, string category, string listPrice) =>
        string.Join(
            AttributeMark, description, "Eaton", "MPN00008", "CTN", category, listPrice, "A");

    /// <summary>A pricing record with one agreement in force for all of 2026.</summary>
    /// <param name="multiplier">Fraction of list.</param>
    /// <returns>The record as stored.</returns>
    public static string Terms(string multiplier) =>
        string.Join(AttributeMark, multiplier, "2026-01-03", "2026-12-29");

    /// <summary>
    /// An inventory record: branches, on hand, committed, on order, bins.
    /// </summary>
    /// <param name="branches">Branch codes, in order.</param>
    /// <param name="onHand">Units present, one per branch.</param>
    /// <param name="committed">Units promised, one per branch.</param>
    /// <returns>The record as stored.</returns>
    /// <remarks>
    /// Written as parallel fields on purpose. Position n of each list describes
    /// the same branch, and a test that built these any other way would not be
    /// exercising the thing that goes wrong.
    /// </remarks>
    public static string Inventory(string[] branches, int[] onHand, int[] committed) =>
        string.Join(
            AttributeMark,
            string.Join(ValueMark, branches),
            string.Join(ValueMark, onHand),
            string.Join(ValueMark, committed),
            string.Join(ValueMark, branches.Select(_ => "0")),
            string.Join(ValueMark, branches.Select(_ => string.Empty)));
}
