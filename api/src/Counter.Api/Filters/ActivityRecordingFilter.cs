namespace Counter.Api.Filters;

using System.Diagnostics;
using Counter.Api.Services;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
/// Records every request against the person who made it.
/// </summary>
/// <remarks>
/// Registered globally so no controller can forget it, and recording failures as
/// well as successes: a failure nobody recorded is indistinguishable from a
/// request nobody made, which is exactly the gap a review will find.
///
/// Nothing here records a credential. The database login is recorded as
/// user@account and never with its password.
/// </remarks>
public sealed class ActivityRecordingFilter(
    ActivityRecorder recorder,
    SessionStore sessions,
    IConfiguration configuration) : IAsyncActionFilter
{
    private readonly ActivityRecorder _recorder = recorder;
    private readonly SessionStore _sessions = sessions;
    private readonly IConfiguration _configuration = configuration;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        Stopwatch timer = Stopwatch.StartNew();
        ActionExecutedContext executed = await next();
        timer.Stop();

        CounterSession session = _sessions.ForRequest(context.HttpContext);

        _recorder.Record(new ActivityEntry(
            OccurredAt: DateTimeOffset.UtcNow,
            UserSubject: session.Persona.Subject,
            DisplayName: session.Persona.DisplayName,
            Action: DescribeAction(context),
            TargetKey: DescribeTarget(context),
            DatabaseLogin: _configuration["Erp:DatabaseLogin"] ?? "u2demo@DEMO",
            DatabaseLoginIsShared: true,
            DurationMs: (int)timer.ElapsedMilliseconds,
            Outcome: DescribeOutcome(executed)));
    }

    /// <summary>Name the action in words a reviewer would recognise.</summary>
    private static string DescribeAction(ActionExecutingContext context) =>
        context.ActionDescriptor.RouteValues["action"] switch
        {
            "Search" => "Searched",
            "Availability" => "Read availability",
            "Commitments" => "Read commitments",
            "Record" => "Read the stored record",
            "Current" => "Read the session",
            "SelectCustomer" => "Selected a customer",
            "SignIn" => "Signed in",
            "Activity" => "Read the activity record",
            string other => other,
            _ => "Unknown",
        };

    /// <summary>Name what the action was about, where there is one.</summary>
    private static string DescribeTarget(ActionExecutingContext context)
    {
        foreach (string key in new[] { "partNumber", "q", "customerAccount" })
        {
            if (context.ActionArguments.TryGetValue(key, out object? value) &&
                value is string text && text.Length > 0)
            {
                return text;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Say how the request went, keeping unreachable distinct from not-found.
    /// </summary>
    /// <remarks>
    /// The same distinction the screen makes, recorded so it survives into the
    /// audit trail. A reviewer asking why a representative quoted no stock needs
    /// to be able to see that the system could not be reached.
    /// </remarks>
    private static string DescribeOutcome(ActionExecutedContext executed) =>
        executed.Exception switch
        {
            null => "Success",
            ErpUnreachableException => "Unreachable",
            ErpRecordNotFoundException => "NotFound",
            ErpRefusedException => "Refused",
            MalformedRecordException => "MalformedRecord",
            _ => "Failed",
        };
}
