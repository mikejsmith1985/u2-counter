namespace Counter.Api.Filters;

using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
/// Turns an ERP failure into a response the client can tell apart from a result.
/// </summary>
/// <remarks>
/// This filter carries the single most important distinction in the application.
/// A failure to reach the data must never arrive as an empty success, because an
/// empty success is indistinguishable from "there is no stock" — and a
/// representative would then tell a customer something untrue while believing the
/// screen.
///
/// So each failure gets its own status and its own type, and the client switches
/// on the type rather than guessing from an empty array.
/// </remarks>
public sealed class ErpProblemFilter : IExceptionFilter
{
    /// <summary>The ERP did not answer in time.</summary>
    public const string UnreachableType = "erp-unreachable";

    /// <summary>The ERP refused the request.</summary>
    public const string RefusedType = "erp-refused";

    /// <summary>The record does not exist.</summary>
    public const string NotFoundType = "not-found";

    /// <summary>A record could not be read without guessing at its meaning.</summary>
    public const string MalformedType = "erp-malformed-record";

    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ProblemDetails? problem = context.Exception switch
        {
            ErpUnreachableException error => Build(
                StatusCodes.Status504GatewayTimeout,
                UnreachableType,
                "The system could not reach the stock data",
                error.Message,
                context),

            ErpRecordNotFoundException error => Build(
                StatusCodes.Status404NotFound,
                NotFoundType,
                "Not found",
                error.Message,
                context),

            ErpRefusedException error => Build(
                StatusCodes.Status502BadGateway,
                RefusedType,
                "The system declined that request",
                error.Message,
                context),

            MalformedRecordException error => Build(
                StatusCodes.Status502BadGateway,
                MalformedType,
                "A record could not be read",
                error.Message,
                context),

            _ => null,
        };

        if (problem is null)
        {
            return;
        }

        context.Result = new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        };

        context.ExceptionHandled = true;
    }

    private static ProblemDetails Build(
        int status,
        string type,
        string title,
        string detail,
        ExceptionContext context) => new()
        {
            Status = status,
            Type = type,
            Title = title,
            // Written for a person to read aloud, because someone on a call will
            // repeat it to the customer waiting.
            Detail = detail,
            Instance = context.HttpContext.Request.Path,
        };
}
