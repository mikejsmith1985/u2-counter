namespace Counter.Api.Controllers;

using Counter.Api.Contracts;
using Counter.Api.Services;
using Counter.Domain.Availability;
using Counter.Domain.Orders;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Everything about a part: finding one, what is available, and what holds it.
/// </summary>
/// <remarks>
/// Every action here is a GET. No route on this controller accepts a verb that
/// could change ERP data, and a test asserts that across the whole application
/// rather than relying on anyone noticing.
/// </remarks>
[ApiController]
[Route("api/v1/parts")]
public sealed class PartsController(
    AvailabilityService availability,
    CommitmentReader commitments,
    AvailabilityReader parts,
    CatalogueProjection catalogue,
    IErpReader erp) : ControllerBase
{
    /// <summary>Most results a search will return, however many are asked for.</summary>
    private const int MaximumSearchResults = 50;

    /// <summary>Results returned when the caller does not say.</summary>
    private const int DefaultSearchResults = 20;

    private readonly AvailabilityService _availability = availability;
    private readonly CommitmentReader _commitments = commitments;
    private readonly AvailabilityReader _parts = parts;
    private readonly CatalogueProjection _catalogue = catalogue;
    private readonly IErpReader _erp = erp;

    /// <summary>
    /// Find parts by number, description or manufacturer.
    /// </summary>
    /// <param name="q">What the user typed.</param>
    /// <param name="limit">Most results to return.</param>
    /// <param name="cancellationToken">Abandons the search when the caller gives up.</param>
    /// <response code="200">Matches, which may legitimately be none.</response>
    [HttpGet]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchResponse>> Search(
        // Nullable deliberately. Left non-nullable, model binding rejects an
        // absent term first and the caller gets the framework's validation
        // problem instead of this controller's -- so the client would have two
        // shapes of 400 to understand where one would do.
        [FromQuery] string? q,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(Problem(
                "invalid-request", "A search needs something to search for."));
        }

        int effectiveLimit = Math.Clamp(
            limit ?? DefaultSearchResults, 1, MaximumSearchResults);

        return Ok(await _availability.SearchAsync(q, effectiveLimit, cancellationToken));
    }

    /// <summary>
    /// Read one part's availability across every branch, with the customer's price.
    /// </summary>
    /// <param name="partNumber">The part to look up.</param>
    /// <param name="customerAccount">Whose price to quote, or omitted for list.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <response code="200">The part, its branches, and the price.</response>
    /// <response code="404">No such part.</response>
    [HttpGet("{partNumber}/availability")]
    [ProducesResponseType<AvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailabilityResponse>> Availability(
        string partNumber,
        [FromQuery] string? customerAccount,
        CancellationToken cancellationToken)
    {
        return Ok(await _availability.ReadAsync(partNumber, customerAccount, cancellationToken));
    }

    /// <summary>
    /// Read what is holding a branch's committed stock for a part.
    /// </summary>
    /// <param name="partNumber">The part.</param>
    /// <param name="branchCode">The branch.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <response code="200">The orders holding stock, and any shortfall.</response>
    /// <response code="404">No such part.</response>
    [HttpGet("{partNumber}/commitments")]
    [ProducesResponseType<CommitmentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommitmentsResponse>> Commitments(
        string partNumber,
        [FromQuery] string branchCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
        {
            return BadRequest(Problem("invalid-request", "A branch code is required."));
        }

        PartAvailability stock = await _parts.ReadAsync(partNumber, cancellationToken);

        BranchPosition? position = stock.Positions.FirstOrDefault(
            candidate => string.Equals(
                candidate.BranchCode, branchCode, StringComparison.OrdinalIgnoreCase));

        if (position is null)
        {
            return NotFound(Problem(
                "not-found", $"{partNumber} holds no position at {branchCode}."));
        }

        BranchCommitments held = await _commitments.ReadAsync(
            partNumber, position.BranchCode, position.Committed, cancellationToken);

        return Ok(new CommitmentsResponse(
            BranchCode: held.BranchCode,
            CommittedTotal: held.CommittedTotal,
            AccountedFor: held.AccountedFor,
            Unaccounted: held.Unaccounted,
            Commitments: held.Commitments
                .Select(commitment => new CommitmentView(
                    commitment.OrderNumber,
                    commitment.CustomerName,
                    commitment.Quantity,
                    commitment.State.ToString(),
                    commitment.PromisedDate))
                .ToList(),
            Envelope: ResponseEnvelope.Complete()));
    }

    /// <summary>
    /// Read the inventory record as the ERP stores it, beside its parsed form.
    /// </summary>
    /// <param name="partNumber">The part.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <response code="200">The raw record, its separators described, and the query.</response>
    /// <response code="404">No such part, or no inventory record for it.</response>
    [HttpGet("{partNumber}/record")]
    [ProducesResponseType<RecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecordResponse>> Record(
        string partNumber,
        CancellationToken cancellationToken)
    {
        if (_catalogue.Find(partNumber) is null)
        {
            return NotFound(Problem("not-found", $"{partNumber} is not in the catalogue."));
        }

        string raw = await _erp.ReadRecordAsync(
            ErpFiles.Inventory.Name, partNumber, cancellationToken);

        MultiValueRecord record = MultiValueRecord.Parse(raw);

        return Ok(new RecordResponse(
            FileName: ErpFiles.Inventory.Name,
            RecordId: partNumber,
            // Carried exactly as stored. The client renders the separators; the
            // contract's job is not to lie about what the record holds.
            RawRecord: raw,
            Marks: DescribeMarksIn(raw),
            Parsed: record.AsFieldMap(),
            Query: $"LIST {ErpFiles.Inventory.Name} {partNumber}",
            Envelope: ResponseEnvelope.Complete()));
    }

    /// <summary>
    /// Describe the separators actually present, so the client can label them.
    /// </summary>
    /// <remarks>
    /// Only the marks the record contains are described. Listing a subvalue mark
    /// for a record that has none would invite the client to explain a structure
    /// that is not there.
    /// </remarks>
    private static IReadOnlyList<MarkDescription> DescribeMarksIn(string raw)
    {
        (char Character, int Code, string Name, string Separates)[] all =
        [
            (MultiValueRecord.AttributeMark, 254, "Attribute mark", "Fields"),
            (MultiValueRecord.ValueMark, 253, "Value mark", "Values within a field"),
            (MultiValueRecord.SubvalueMark, 252, "Subvalue mark", "Sub-items within a value"),
        ];

        return all
            .Where(mark => raw.Contains(mark.Character, StringComparison.Ordinal))
            .Select(mark => new MarkDescription(
                mark.Character.ToString(), mark.Code, mark.Name, mark.Separates))
            .ToList();
    }

    /// <summary>Build a problem detail with a type the client switches on.</summary>
    private ProblemDetails Problem(string type, string detail) => new()
    {
        Type = type,
        Title = type,
        Detail = detail,
        Status = type == "not-found" ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
        Instance = HttpContext.Request.Path,
    };
}
