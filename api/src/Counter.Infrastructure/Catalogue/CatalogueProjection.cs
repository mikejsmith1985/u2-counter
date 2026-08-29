namespace Counter.Infrastructure.Catalogue;

using System.Globalization;
using System.Text;
using Counter.Domain.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.Logging;

/// <summary>
/// The catalogue held in memory, so that search answers instantly.
/// </summary>
/// <remarks>
/// This is a deliberate line, and worth stating precisely: caching <em>what
/// exists</em> is safe, caching <em>how much there is</em> is not. The catalogue
/// changes rarely and a stale entry is at worst a part that is no longer sold.
/// Stock changes constantly and a stale quantity is a promise to a customer that
/// cannot be kept.
///
/// So every quantity, price and commitment is read live, every time. Only the
/// searchable text lives here.
/// </remarks>
public sealed class CatalogueProjection(IErpReader erp, ILogger<CatalogueProjection> logger)
{
    /// <summary>Most parts to load. Guards against an unexpectedly vast catalogue.</summary>
    private const int MaximumParts = 50_000;

    /// <summary>
    /// Records read per call. Large enough that three thousand parts take a few
    /// round trips, small enough that one response stays a manageable size.
    /// </summary>
    private const int BatchSize = 250;

    private readonly IErpReader _erp = erp;
    private readonly ILogger<CatalogueProjection> _logger = logger;
    private readonly SemaphoreSlim _buildGate = new(1, 1);

    private IReadOnlyList<CatalogueEntry> _entries = [];

    /// <summary>How many parts are searchable.</summary>
    public int Count => _entries.Count;

    /// <summary>Whether the projection has been built.</summary>
    public bool IsBuilt => _entries.Count > 0;

    /// <summary>
    /// Build the projection if it is not already built.
    /// </summary>
    /// <param name="cancellationToken">Abandons the build when the caller gives up.</param>
    /// <remarks>
    /// Startup builds the catalogue, but a slow or unavailable ERP at that moment
    /// must not leave search broken for the life of the process. Checking here
    /// means the first request after the ERP recovers rebuilds it, rather than
    /// someone having to notice and restart the application.
    /// </remarks>
    public async Task EnsureBuiltAsync(CancellationToken cancellationToken)
    {
        if (IsBuilt)
        {
            return;
        }

        await BuildAsync(cancellationToken);
    }

    /// <summary>
    /// Read the catalogue from the ERP and hold its searchable form.
    /// </summary>
    /// <param name="cancellationToken">Abandons the build when the caller gives up.</param>
    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        await _buildGate.WaitAsync(cancellationToken);
        try
        {
            // Keys first, then the records in batches. A LIST would return the
            // records formatted for a person to read, with the separators turned
            // into newlines -- right on a screen, and useless here, because the
            // structure those separators carried has gone.
            IReadOnlyList<string> keys = await _erp.SelectKeysAsync(
                $"SELECT {ErpFiles.Product.Name}", MaximumParts, cancellationToken);

            List<CatalogueEntry> entries = new(keys.Count);

            foreach (string[] batch in keys.Chunk(BatchSize))
            {
                IReadOnlyDictionary<string, string> records =
                    await _erp.ReadRecordsAsync(ErpFiles.Product.Name, batch, cancellationToken);

                entries.AddRange(records.Select(record =>
                    new CatalogueEntry(AvailabilityReader.ReadPart(record.Key, record.Value))));
            }

            _entries = entries.OrderBy(entry => entry.Part.PartNumber, StringComparer.Ordinal)
                              .ToList();

            _logger.LogInformation("Catalogue projection built with {Count} parts", _entries.Count);

            if (_entries.Count < keys.Count)
            {
                // Fewer parts than keys means some records could not be read, and
                // search will silently fail to find them -- worse than a slow search.
                _logger.LogWarning(
                    "{Missing} of {Total} catalogue records could not be read",
                    keys.Count - _entries.Count,
                    keys.Count);
            }
        }
        finally
        {
            _buildGate.Release();
        }
    }

    /// <summary>
    /// Find parts matching what someone typed.
    /// </summary>
    /// <param name="text">Part number, description words, or manufacturer.</param>
    /// <param name="limit">Most results to return.</param>
    /// <returns>Matches, best first.</returns>
    public IReadOnlyList<Part> Search(string text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string normalised = Normalise(text);
        string[] words = Words(text);

        return _entries
            .Select(entry => new { entry.Part, Rank = entry.RankAgainst(normalised, words) })
            .Where(match => match.Rank > 0)
            .OrderByDescending(match => match.Rank)
            .ThenBy(match => match.Part.PartNumber, StringComparer.Ordinal)
            .Take(limit)
            .Select(match => match.Part)
            .ToList();
    }

    /// <summary>Look up one part by its exact number.</summary>
    /// <param name="partNumber">The key.</param>
    public Part? Find(string partNumber)
    {
        string normalised = Normalise(partNumber);

        return _entries.FirstOrDefault(entry => entry.NormalisedPartNumber == normalised)?.Part;
    }

    /// <summary>
    /// Reduce text to what a match should ignore.
    /// </summary>
    /// <remarks>
    /// Customers read part numbers off a box, a phone screen or their own memory,
    /// and the punctuation rarely survives. Matching on letters and digits alone
    /// means "sqd qo120", "SQD-QO120" and "sqdqo120" all find the same part.
    /// </remarks>
    public static string Normalise(string text)
    {
        StringBuilder builder = new(text.Length);

        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Split what someone typed into the words a match must contain.
    /// </summary>
    /// <param name="text">What they typed.</param>
    /// <remarks>
    /// Words rather than the string itself, because the string is whatever
    /// reached the box. Someone types "gfci breaker", or "breaker gfci", or
    /// pastes with a space on each end, and all three mean the same request; a
    /// substring match honours only the first of them.
    /// </remarks>
    public static string[] Words(string text) =>
        text.Split(
            [' ', '\t', ',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToUpperInvariant())
            .ToArray();
}

/// <summary>
/// One searchable catalogue entry, with its text prepared for matching.
/// </summary>
/// <param name="Part">The part itself.</param>
internal sealed record CatalogueEntry(Part Part)
{
    /// <summary>The part number with punctuation removed, for forgiving matches.</summary>
    public string NormalisedPartNumber { get; } = CatalogueProjection.Normalise(Part.PartNumber);

    private string SearchableText { get; } =
        $"{Part.PartNumber} {Part.Description} {Part.Manufacturer} {Part.ManufacturerPartNumber}"
            .ToUpperInvariant();

    /// <summary>
    /// How well this entry matches, with zero meaning not at all.
    /// </summary>
    /// <param name="normalisedQuery">What was typed, letters and digits only.</param>
    /// <param name="words">What was typed, split into words and upper-cased.</param>
    /// <remarks>
    /// Ranked rather than filtered, so an exact part number outranks a part that
    /// merely mentions the same word in its description. Someone who types a part
    /// number wants that part first.
    /// </remarks>
    public int RankAgainst(string normalisedQuery, string[] words)
    {
        const int ExactPartNumber = 100;
        const int PartNumberPrefix = 50;
        const int PartNumberContains = 25;
        const int TextContains = 10;

        if (NormalisedPartNumber == normalisedQuery)
        {
            return ExactPartNumber;
        }

        if (NormalisedPartNumber.StartsWith(normalisedQuery, StringComparison.Ordinal))
        {
            return PartNumberPrefix;
        }

        if (NormalisedPartNumber.Contains(normalisedQuery, StringComparison.Ordinal))
        {
            return PartNumberContains;
        }

        // Every word, in any order. Requiring all of them keeps a two-word
        // search narrower than a one-word search, which is what someone adding
        // a word to the box is asking for.
        bool matchesEveryWord = words.Length > 0 && words.All(
            word => SearchableText.Contains(word, StringComparison.Ordinal));

        return matchesEveryWord ? TextContains : 0;
    }
}
