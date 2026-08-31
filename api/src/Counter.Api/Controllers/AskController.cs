namespace Counter.Api.Controllers;

using Counter.Api.Services;
using Counter.Infrastructure.Ai;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// A question in words, answered from the ERP with the working shown.
/// </summary>
/// <remarks>
/// The endpoint that makes the MCP server load-bearing. Every answer here is
/// assembled from tool calls the model made, and every one of those calls is
/// returned alongside the answer — including the raw record, separators intact.
///
/// That transcript is the point. A reader looking at "299 free to sell at Grand
/// Junction" has no way to know whether it came from a MultiValue database or
/// from a relational table wearing separators; the only thing that settles it is
/// showing the call and the bytes that came back.
/// </remarks>
/// <param name="ask">The assistant.</param>
/// <param name="sessions">Who is asking, and how much they have asked.</param>
[ApiController]
[Route("api/v1/ask")]
public sealed class AskController(AskService ask, SessionStore sessions) : ControllerBase
{
    private readonly AskService _ask = ask;
    private readonly SessionStore _sessions = sessions;

    /// <summary>
    /// Say whether there is an assistant, before anyone types into a box.
    /// </summary>
    /// <response code="200">Whether questions can be asked, and which model answers.</response>
    /// <remarks>
    /// So the box can be absent rather than broken. An input that accepts a
    /// question and then reports that no assistant exists has wasted somebody's
    /// typing to tell them something it knew before they started.
    /// </remarks>
    [HttpGet("status")]
    [ProducesResponseType<AskStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<AskStatusResponse> Status() =>
        Ok(new AskStatusResponse(_ask.IsConfigured, AskService.Model));

    /// <summary>
    /// Answer one question.
    /// </summary>
    /// <param name="request">What was asked.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <response code="200">The answer, and every call it took.</response>
    /// <response code="429">A limit was reached. Which one is in the body.</response>
    [HttpPost]
    [ProducesResponseType<AskResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AskResult>> Ask(
        [FromBody] AskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        (AskResult? result, AskRefusal refusal) = await _ask.AnswerAsync(
            request.Question ?? string.Empty,
            request.Looking ?? ScreenContext.Empty,
            _sessions.QuestionsAsked(HttpContext),
            cancellationToken);

        if (refusal != AskRefusal.None)
        {
            return Refused(refusal);
        }

        _sessions.RecordQuestion(HttpContext);

        return Ok(result);
    }

    /// <summary>
    /// Turn a refusal into the response that explains it.
    /// </summary>
    /// <param name="refusal">Why nothing was answered.</param>
    /// <returns>The response to send.</returns>
    /// <remarks>
    /// Every one of these is the system working rather than failing, so each says
    /// what happened and what to do about it. "Come back tomorrow" and "this was
    /// never switched on" are not the same message and must not share one.
    /// </remarks>
    private ActionResult<AskResult> Refused(AskRefusal refusal) => refusal switch
    {
        AskRefusal.NotConfigured => StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            Problem(
                "assistant-not-configured",
                "No assistant is configured on this deployment. Everything else works.")),

        AskRefusal.SessionLimitReached => StatusCode(
            StatusCodes.Status429TooManyRequests,
            Problem(
                "session-limit-reached",
                "This session has asked its allowance of questions. The demonstration " +
                "runs on a personal API key, so the allowance is small on purpose.")),

        AskRefusal.DailyLimitReached => StatusCode(
            StatusCodes.Status429TooManyRequests,
            Problem(
                "daily-limit-reached",
                "The assistant has spent its allowance for today. Everything else on " +
                "this screen still works; the assistant returns tomorrow.")),

        _ => BadRequest(Problem("nothing-asked", "There was no question to answer.")),
    };

    /// <summary>Build a problem body in the shape this API uses everywhere.</summary>
    private static object Problem(string type, string detail) =>
        new { type, title = "The assistant did not answer", detail };
}

/// <summary>What was asked.</summary>
/// <param name="Question">The question, in words.</param>
/// <param name="Looking">
/// What the person had on screen, so a question phrased the way people phrase
/// them at a counter -- "twenty of these to Denver" -- resolves to the part in
/// front of them instead of coming back as "which part did you mean?".
///
/// Optional, and absent is a supported state: with nothing selected the
/// assistant works exactly as it did before.
/// </param>
public sealed record AskRequest(string? Question, ScreenContext? Looking);

/// <summary>Whether questions can be asked at all.</summary>
/// <param name="IsConfigured">Whether an assistant exists on this deployment.</param>
/// <param name="Model">Which model answers. Reported so nobody has to take it on trust.</param>
public sealed record AskStatusResponse(bool IsConfigured, string Model);
