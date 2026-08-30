namespace Counter.Api.Controllers;

using Counter.Domain.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// What is in this account, and what its fields mean — asked of the database.
/// </summary>
/// <remarks>
/// The screens elsewhere in this application are built to one layout, compiled
/// in. That is right for a counter: the people using it want the same four
/// numbers in the same place every time, and a screen that rearranged itself
/// would be worse at the job.
///
/// It is wrong for anybody evaluating this against their own data. Their files
/// are not called PRODUCT and INVENTORY, and their fields are not in these
/// positions — so a fixed layout shows them nothing about their own system.
///
/// A MultiValue database already carries the answer. Every file has a dictionary
/// saying which position holds what, how to display it, and whether it is
/// multi-valued, so a screen built from the dictionary fits whichever account it
/// is pointed at. Nothing here knows a single field name in advance.
/// </remarks>
/// <param name="erp">The MCP client.</param>
[ApiController]
[Route("api/v1/schema")]
public sealed class SchemaController(IErpReader erp) : ControllerBase
{
    /// <summary>Most keys to select when exploring by a field.</summary>
    private const int MaximumKeys = 40;

    /// <summary>Most records to read back and show.</summary>
    private const int MaximumRecords = 20;

    private readonly IErpReader _erp = erp;

    /// <summary>
    /// Name the files in the account.
    /// </summary>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <response code="200">The file names, sorted.</response>
    [HttpGet("files")]
    [ProducesResponseType<FilesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FilesResponse>> Files(CancellationToken cancellationToken) =>
        Ok(new FilesResponse(await _erp.ListFilesAsync(cancellationToken)));

    /// <summary>
    /// Describe one file, from its own dictionary.
    /// </summary>
    /// <param name="fileName">The file to describe.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <response code="200">Its fields, in position order.</response>
    [HttpGet("files/{fileName}")]
    [ProducesResponseType<DictionaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DictionaryResponse>> Describe(
        string fileName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DictionaryField> fields =
            await _erp.ListDictionaryAsync(fileName, cancellationToken);

        return Ok(new DictionaryResponse(fileName, fields));
    }

    /// <summary>
    /// Find records by a field the dictionary named.
    /// </summary>
    /// <param name="fileName">Which file to search.</param>
    /// <param name="position">Which field, by the position its dictionary gives.</param>
    /// <param name="value">What to match.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <response code="200">Matching records, with the values in dictionary order.</response>
    /// <remarks>
    /// The selection is parameterised, not composed. The field is named by the
    /// position its own dictionary reported and the value is quoted, so nothing
    /// a caller types becomes part of the query's structure — which is what
    /// keeps this a search rather than the arbitrary-query tool this application
    /// deliberately does not permit.
    /// </remarks>
    [HttpGet("files/{fileName}/records")]
    [ProducesResponseType<RecordsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RecordsResponse>> Records(
        string fileName,
        [FromQuery] int? position,
        [FromQuery] string? value,
        [FromQuery] bool? exact,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DictionaryField> fields =
            await _erp.ListDictionaryAsync(fileName, cancellationToken);

        bool isFiltered = position is > 0 && !string.IsNullOrWhiteSpace(value);

        // Contains, unless asked for an exact match.
        //
        // Exact was the only option and it is what a MultiValue SELECT does, but
        // as the sole behaviour on a screen for exploring an unfamiliar account
        // it reads as broken: typing "aurora" against a branch called "Aurora"
        // returned nothing at all, with no hint why.
        //
        // LIKE is the operator UniVerse already has, with three dots as its
        // wildcard, so this invents no syntax. Case stays significant either
        // way -- quietly folding case here would make the screen behave unlike
        // the database it is demonstrating, and the reader could not reproduce
        // it against their own.
        string selection;

        if (!isFiltered)
        {
            selection = $"SELECT {fileName}";
        }
        else if (exact == true)
        {
            selection = $"SELECT {fileName} WITH F{position} = \"{Quoted(value!)}\"";
        }
        else
        {
            selection = $"SELECT {fileName} WITH F{position} LIKE \"...{Quoted(value!)}...\"";
        }

        IReadOnlyList<string> keys =
            await _erp.SelectKeysAsync(selection, MaximumKeys, cancellationToken);

        string[] wanted = [.. keys.Take(MaximumRecords)];

        IReadOnlyDictionary<string, string> records =
            await _erp.ReadRecordsAsync(fileName, wanted, cancellationToken);

        RecordRow[] rows =
        [
            .. wanted
                .Where(records.ContainsKey)
                .Select(key => new RecordRow(
                    key,
                    records[key],
                    SplitFields(records[key]))),
        ];

        return Ok(new RecordsResponse(fileName, fields, rows, keys.Count, selection));
    }

    /// <summary>
    /// Split a record into its fields.
    /// </summary>
    /// <param name="raw">The record as stored.</param>
    /// <returns>One entry per field, in order.</returns>
    /// <remarks>
    /// Split only on the attribute mark. The values inside a field stay together
    /// on purpose: a parallel field's values line up with another field's, and
    /// splitting them apart here would throw away the alignment the caller needs
    /// in order to show them side by side.
    /// </remarks>
    private static IReadOnlyList<string> SplitFields(string raw) =>
        raw.Length == 0 ? [] : raw.Split(MultiValueRecord.AttributeMark);

    /// <summary>
    /// Make a value safe to sit inside a quoted selection.
    /// </summary>
    /// <param name="value">What the caller typed.</param>
    /// <returns>The value with its quotes removed.</returns>
    /// <remarks>
    /// The only character that could end the quoted string early is the quote
    /// itself, so it is removed rather than escaped — MultiValue query syntax has
    /// no escape for it, and a value containing one is a typo rather than a
    /// legitimate search.
    /// </remarks>
    private static string Quoted(string value) =>
        value.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
}

/// <summary>The files in the account.</summary>
/// <param name="Files">Their names, sorted.</param>
public sealed record FilesResponse(IReadOnlyList<string> Files);

/// <summary>One file, as its dictionary describes it.</summary>
/// <param name="File">The file's name.</param>
/// <param name="Fields">Its fields, in position order.</param>
public sealed record DictionaryResponse(string File, IReadOnlyList<DictionaryField> Fields);

/// <summary>One record, raw and split.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Raw">The record exactly as stored, separators included.</param>
/// <param name="Fields">Its fields, split on the attribute mark.</param>
public sealed record RecordRow(string Key, string Raw, IReadOnlyList<string> Fields);

/// <summary>What a search of a file returned.</summary>
/// <param name="File">The file searched.</param>
/// <param name="Dictionary">Its fields, so a caller can label the values.</param>
/// <param name="Records">The records read.</param>
/// <param name="MatchCount">How many keys matched, which may exceed those read.</param>
/// <param name="Selection">
/// The selection that ran, shown to the caller. A screen that says what it asked
/// is one whose answer can be checked.
/// </param>
public sealed record RecordsResponse(
    string File,
    IReadOnlyList<DictionaryField> Dictionary,
    IReadOnlyList<RecordRow> Records,
    int MatchCount,
    string Selection);
