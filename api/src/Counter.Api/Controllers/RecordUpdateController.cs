namespace Counter.Api.Controllers;

using Counter.Infrastructure.Mcp;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Changing one value in a record, with the change shown before and after.
/// </summary>
/// <remarks>
/// The write half of find-review-update. Everything else in this application
/// reads, and this is deliberately the only route that does not.
///
/// It is absent unless a deployment configures a writable ERP driver. The
/// demonstration does not, so on the deployed application <see cref="IErpWriter"/>
/// is not registered and this reports that plainly — which is the honest
/// behaviour: a write endpoint that exists but refuses everything invites the
/// question of what else is switched off.
///
/// Two things happen on every change that would not happen in a relational
/// system. The record is read immediately before, so the caller is told what
/// they are about to replace rather than what they saw a minute ago. And it is
/// read back afterwards and compared, because MultiValue has no constraints to
/// break — a record that is now wrong is still a valid record, so looking is the
/// only confirmation available.
/// </remarks>
/// <param name="writer">The write path, absent when writes are not configured.</param>
[ApiController]
[Route("api/v1/records")]
public sealed class RecordUpdateController(IErpWriter? writer = null) : ControllerBase
{
    private readonly IErpWriter? _writer = writer;

    /// <summary>
    /// Say whether this deployment can change anything.
    /// </summary>
    /// <response code="200">Whether a write path is configured.</response>
    /// <remarks>
    /// Asked before an edit control is offered, so a reader is not invited to
    /// type a change that cannot be made.
    /// </remarks>
    [HttpGet("status")]
    [ProducesResponseType<UpdateStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<UpdateStatusResponse> Status() =>
        Ok(new UpdateStatusResponse(_writer is not null));

    /// <summary>
    /// Change one value of one field.
    /// </summary>
    /// <param name="fileName">The MultiValue file.</param>
    /// <param name="recordId">The record's key.</param>
    /// <param name="request">Which value, and what to put there.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <response code="200">The record before and after, with both field shapes.</response>
    /// <response code="409">The ERP declined. Nothing was written.</response>
    /// <response code="501">This deployment has no write path configured.</response>
    [HttpPost("{fileName}/{recordId}/value")]
    [ProducesResponseType<RecordChange>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RecordChange>> UpdateValue(
        string fileName,
        string recordId,
        [FromBody] UpdateValueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_writer is null)
        {
            return StatusCode(
                StatusCodes.Status501NotImplemented,
                Problem(
                    "writes-not-configured",
                    "This deployment reads only. It is running the read-only ERP driver, " +
                    "which has no write path at all — not a switched-off one."));
        }

        try
        {
            RecordChange change = await _writer.UpdateValueAsync(
                fileName,
                recordId,
                request.Position,
                request.Index,
                request.Value ?? string.Empty,
                cancellationToken);

            return Ok(change);
        }
        catch (ErpWriteRefusedException refusal)
        {
            // A refusal is the system working, not failing. Reported as a
            // conflict rather than an error because retrying will not help and
            // the screen should not offer to.
            return Conflict(Problem("change-refused", refusal.Message));
        }
    }

    /// <summary>Build a problem body in the shape this API uses everywhere.</summary>
    private static object Problem(string type, string detail) =>
        new { type, title = "The change was not made", detail };
}

/// <summary>Which value to change, and what to put there.</summary>
/// <param name="Position">Which field, counting from one.</param>
/// <param name="Index">Which value within that field, counting from zero.</param>
/// <param name="Value">What to put there.</param>
public sealed record UpdateValueRequest(int Position, int Index, string? Value);

/// <summary>Whether this deployment can change anything.</summary>
/// <param name="CanWrite">True only when a write path is configured.</param>
public sealed record UpdateStatusResponse(bool CanWrite);
