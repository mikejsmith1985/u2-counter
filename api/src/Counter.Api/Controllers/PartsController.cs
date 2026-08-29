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
    /// <summary>
    /// List the catalogue, for somebody who has not got a part number yet.
    /// </summary>
    /// <param name="limit">Most parts to return.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <response code="200">The first parts, and how many there are altogether.</response>
    /// <remarks>
    /// A separate route from searching rather than a search with the term left
    /// out, because they answer different questions. Searching answers "where is
    /// this"; browsing answers "what is here", and only browsing has any business
    /// reporting a total.
    ///
    /// Keeping them apart also leaves the search guard intact. A search with an
    /// empty term is still a bad request, and that matters: a query of
    /// punctuation once returned the entire catalogue ranked as though every row
    /// were a strong match.
    ///
    /// No quantities. Stock is read live, every time, because a cached quantity is
    /// a promise to a customer that cannot be kept -- and a picker exists to find
    /// a part, not to report on one.
    /// </remarks>
    [HttpGet("browse")]
    [ProducesResponseType<BrowseResponse<PartSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrowseResponse<PartSummary>>> Browse(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        await _catalogue.EnsureBuiltAsync(cancellationToken);

        int effectiveLimit = Math.Clamp(
            limit ?? DefaultSearchResults, 1, MaximumSearchResults);

        IReadOnlyList<PartSummary> page = _catalogue
            .Browse(effectiveLimit)
            .Select(part => new PartSummary(
                part.PartNumber,
                part.Description,
                part.Manufacturer,
                part.ManufacturerPartNumber,
                part.UnitOfMeasure,
                part.IsDiscontinued))
            .ToList();

        return Ok(new BrowseResponse<PartSummary>(
            page, _catalogue.Count, ResponseEnvelope.Complete()));
    }

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
            // The unaccounted figure is only a discrepancy if every order that
            // could explain it was read. Saying so is the difference between a
            // representative ringing the branch about a real problem and ringing
            // them about a truncated list.
            Envelope: held.WasCapped
                ? ResponseEnvelope.Partial(
                    $"More than {CommitmentReader.MaximumOrdersRead} orders reference this part. " +
                    "Some are not listed, so the unaccounted figure may be overstated.")
                : ResponseEnvelope.Complete()));
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
    /// Describe the separators present, in the shape the contract promises.
    /// </summary>
    /// <remarks>
    /// Which marks a record holds is a fact about the record format, so it is
    /// answered by <see cref="RecordMarks"/>. This turns that answer into the
    /// contract's shape and does nothing else.
    /// </remarks>
    private static IReadOnlyList<MarkDescription> DescribeMarksIn(string raw) =>
        RecordMarks.PresentIn(raw)
            .Select(mark => new MarkDescription(
                mark.Character.ToString(), mark.Code, mark.Name, mark.Separates))
            .ToList();

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
