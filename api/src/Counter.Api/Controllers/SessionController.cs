namespace Counter.Api.Controllers;

using Counter.Api.Services;
using Counter.Infrastructure.Mcp;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Who is signed in, and which customer they are serving.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class SessionController(
    SessionStore sessions,
    ActivityRecorder activity,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>Most activity entries to return.</summary>
    private const int MaximumActivityEntries = 100;

    private readonly SessionStore _sessions = sessions;
    private readonly ActivityRecorder _activity = activity;
    private readonly IConfiguration _configuration = configuration;

    /// <summary>
    /// Read the current session.
    /// </summary>
    /// <response code="200">Identity, permissions, and the database login in use.</response>
    [HttpGet("session")]
    [ProducesResponseType<SessionView>(StatusCodes.Status200OK)]
    public ActionResult<SessionView> Current()
    {
        CounterSession session = _sessions.ForRequest(HttpContext);
        return Ok(Describe(session));
    }

    /// <summary>
    /// Sign in as one of the demonstration personas.
    /// </summary>
    /// <param name="request">Which persona.</param>
    /// <response code="200">The new session.</response>
    /// <response code="404">No such persona.</response>
    [HttpPost("session")]
    [ProducesResponseType<SessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SessionView> SignIn([FromBody] SignInRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Persona? persona = DemonstrationPersonas.Find(request.Subject);
        if (persona is null)
        {
            return NotFound(new ProblemDetails
            {
                Type = "not-found",
                Title = "No such persona",
                Detail = $"'{request.Subject}' is not one of the demonstration personas.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        return Ok(Describe(_sessions.SignIn(HttpContext, persona)));
    }

    /// <summary>
    /// List the personas this demonstration offers.
    /// </summary>
    /// <response code="200">Every persona, with what each is useful for showing.</response>
    [HttpGet("session/personas")]
    [ProducesResponseType<IReadOnlyList<Persona>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<Persona>> Personas() => Ok(DemonstrationPersonas.All);

    /// <summary>
    /// Choose the customer being served.
    /// </summary>
    /// <param name="request">The account, or null to clear it.</param>
    /// <remarks>
    /// One of the two routes in this application that are not a GET — the other
    /// signs a persona in, above. Both write to this application's own session
    /// and neither touches the ERP, which is the distinction that matters:
    /// `ReadOnlyRouteTests` permits exactly these two by name, so a third would
    /// fail the build rather than joining a list.
    /// </remarks>
    /// <response code="200">The updated session.</response>
    [HttpPut("session/customer")]
    [ProducesResponseType<SessionView>(StatusCodes.Status200OK)]
    public ActionResult<SessionView> SelectCustomer([FromBody] SelectCustomerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        CounterSession session = _sessions.SelectCustomer(HttpContext, request.CustomerAccount);
        return Ok(Describe(session));
    }

    /// <summary>
    /// Read recent activity for the signed-in user.
    /// </summary>
    /// <param name="limit">Most entries to return.</param>
    /// <response code="200">What this person has asked of the system.</response>
    [HttpGet("activity")]
    [ProducesResponseType<ActivityView>(StatusCodes.Status200OK)]
    public ActionResult<ActivityView> Activity([FromQuery] int? limit)
    {
        CounterSession session = _sessions.ForRequest(HttpContext);
        int effectiveLimit = Math.Clamp(limit ?? 20, 1, MaximumActivityEntries);

        return Ok(new ActivityView(
            _activity.Recent(session.Persona.Subject, effectiveLimit)
                .Select(entry => new ActivityEntryView(
                    entry.OccurredAt,
                    entry.DisplayName,
                    entry.Action,
                    entry.TargetKey,
                    entry.DatabaseLogin,
                    entry.DatabaseLoginIsShared,
                    entry.DurationMs,
                    entry.Outcome))
                .ToList()));
    }

    /// <summary>Describe a session for the client.</summary>
    private SessionView Describe(CounterSession session)
    {
        // Read from configuration rather than assumed, so the strip reports the
        // account actually in use rather than one someone wrote down once.
        string login = _configuration["Erp:DatabaseLogin"] ?? "u2demo@DEMO";

        return new SessionView(
            DisplayName: session.Persona.DisplayName,
            UserSubject: session.Persona.Subject,
            HomeBranchCode: session.Persona.HomeBranchCode,
            SelectedCustomerAccount: session.SelectedCustomerAccount,
            IsReadOnly: session.Persona.IsReadOnly,
            DatabaseLogin: login,
            // Stated plainly rather than glossed over. One demonstration account
            // serves everyone here, so the database cannot tell callers apart —
            // which is exactly the limitation a reviewer should see.
            DatabaseLoginIsShared: true,
            IsDemonstrationData: true);
    }
}

/// <summary>Which persona to sign in as.</summary>
/// <param name="Subject">The persona's identifier.</param>
public sealed record SignInRequest(string Subject);

/// <summary>Which customer is being served.</summary>
/// <param name="CustomerAccount">The account, or null to clear it.</param>
public sealed record SelectCustomerRequest(string? CustomerAccount);

/// <summary>The session, as the governance strip shows it.</summary>
public sealed record SessionView(
    string DisplayName,
    string UserSubject,
    string HomeBranchCode,
    string? SelectedCustomerAccount,
    bool IsReadOnly,
    string DatabaseLogin,
    bool DatabaseLoginIsShared,
    bool IsDemonstrationData);

/// <summary>One recorded request, as the activity panel shows it.</summary>
public sealed record ActivityEntryView(
    DateTimeOffset OccurredAt,
    string DisplayName,
    string Action,
    string TargetKey,
    string DatabaseLogin,
    bool DatabaseLoginIsShared,
    int DurationMs,
    string Outcome);

/// <summary>Recent activity.</summary>
/// <param name="Entries">Newest first.</param>
public sealed record ActivityView(IReadOnlyList<ActivityEntryView> Entries);
