namespace Counter.Api.Controllers;

using Counter.Api.Services;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Mcp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>
/// Whether this application can answer the question it exists to answer.
/// </summary>
/// <remarks>
/// A process being alive is not health. This application starts, begins reading
/// the catalogue in the background, and serves requests throughout — so there is
/// a window in which the port is open, every route responds, and search returns
/// nothing. A probe that only checked the port would call that healthy and route
/// traffic to it.
///
/// So readiness means the catalogue is built. Liveness is separate and cheaper:
/// a process that is running should not be restarted merely because the ERP is
/// unreachable, since restarting it will not make the ERP reachable and will
/// throw away the catalogue it already has.
/// </remarks>
/// <param name="catalogue">What has been read, and whether it is ready.</param>
/// <param name="erp">Where this instance believes the ERP is.</param>
/// <param name="activity">Whether the audit trail is reaching a durable store.</param>
[ApiController]
[Route("health")]
public sealed class HealthController(
    CatalogueProjection catalogue,
    IOptions<ErpConnectionOptions> erp,
    ActivityRecorder activity) : ControllerBase
{
    private readonly CatalogueProjection _catalogue = catalogue;
    private readonly ErpConnectionOptions _erp = erp.Value;
    private readonly ActivityRecorder _activity = activity;

    /// <summary>
    /// Whether this instance should be given traffic.
    /// </summary>
    /// <response code="200">The catalogue is readable, so search can answer.</response>
    /// <response code="503">Still starting, or the catalogue could not be read.</response>
    [HttpGet]
    [ProducesResponseType<HealthView>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthView>(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<HealthView> Ready()
    {
        HealthView view = new(
            IsReady: _catalogue.IsBuilt,
            CatalogueCount: _catalogue.Count,
            // Reported because "which ERP is this pointed at, and how long will
            // it wait" is the first question anyone asks when an instance is
            // behaving unlike its neighbour. No credential is here: the endpoint
            // is an address, and the server holds the login.
            ErpEndpoint: _erp.Endpoint.ToString(),
            RequestBudgetSeconds: _erp.RequestBudget.TotalSeconds,
            // Whether the record of who asked what will survive this container.
            // Running without a durable store is supported, and the difference
            // is exactly the sort of thing that is discovered at the worst
            // moment unless something says it out loud.
            IsAuditDurable: _activity.IsDurable,
            Detail: _catalogue.IsBuilt
                ? "The catalogue is readable and search can answer."
                : "The catalogue has not been read yet, so search would return nothing.");

        return view.IsReady
            ? Ok(view)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, view);
    }

    /// <summary>
    /// Whether this instance is running at all.
    /// </summary>
    /// <remarks>
    /// Deliberately answers without consulting anything. Failing this restarts
    /// the container, and there is no failure of the ERP that a restart would
    /// improve — it would only discard a catalogue that had been read.
    /// </remarks>
    /// <response code="200">The process is running.</response>
    [HttpGet("live")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Live() => Ok(new { isAlive = true });
}

/// <summary>What the readiness probe reports.</summary>
/// <param name="IsReady">Whether this instance should be given traffic.</param>
/// <param name="CatalogueCount">How many parts are searchable.</param>
/// <param name="ErpEndpoint">Where this instance believes the ERP is.</param>
/// <param name="RequestBudgetSeconds">How long it will wait for one.</param>
/// <param name="IsAuditDurable">
/// Whether the audit trail reaches a durable store. False is a supported way to
/// run — everything works except surviving a restart — and saying so here is what
/// keeps it from being discovered later by someone looking for a record that was
/// never kept.
/// </param>
/// <param name="Detail">Why, in a sentence, for whoever is reading a probe log.</param>
public sealed record HealthView(
    bool IsReady,
    int CatalogueCount,
    string ErpEndpoint,
    double RequestBudgetSeconds,
    bool IsAuditDurable,
    string Detail);
