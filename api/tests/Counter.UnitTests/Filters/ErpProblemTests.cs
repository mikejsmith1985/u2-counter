namespace Counter.UnitTests.Filters;

using Counter.Api.Filters;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Telling a failure apart from an answer.
/// </summary>
/// <remarks>
/// This filter carries the single most important distinction in the
/// application: a failure to reach the data must never arrive as an empty
/// success, because an empty success is indistinguishable from "there is no
/// stock". A representative reading that screen would tell a customer something
/// untrue while believing it.
///
/// Each failure therefore gets its own status and its own type, and the client
/// switches on the type rather than guessing from an empty array. What makes
/// this worth pinning is that every wrong answer here looks fine: a timeout
/// reported as 404 reads as "no such part", and a 404 reported as 502 sends
/// somebody to check a database that is working perfectly.
/// </remarks>
public sealed class ErpProblemTests
{
    /// <summary>Run the filter over one exception.</summary>
    /// <param name="thrown">What went wrong.</param>
    /// <param name="path">The request path, which the problem should name.</param>
    /// <returns>The context afterwards, for reading what it decided.</returns>
    private static ExceptionContext Handle(Exception thrown, string path = "/api/v1/parts/AUR")
    {
        DefaultHttpContext http = new();
        http.Request.Path = path;

        ExceptionContext context = new(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [])
        {
            Exception = thrown,
        };

        new ErpProblemFilter().OnException(context);

        return context;
    }

    /// <summary>Read the problem the filter produced.</summary>
    /// <param name="context">The handled context.</param>
    /// <returns>The problem details.</returns>
    private static ProblemDetails ProblemFrom(ExceptionContext context) =>
        Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(context.Result).Value);

    [Fact]
    public void An_unreachable_database_is_a_gateway_timeout_and_says_which_kind()
    {
        // Not a 500 and not an empty list. The client has to be able to say
        // "the stock system did not answer" rather than "there is none".
        ExceptionContext context = Handle(new ErpUnreachableException("Timed out after 5s."));

        ProblemDetails problem = ProblemFrom(context);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, problem.Status);
        Assert.Equal(ErpProblemFilter.UnreachableType, problem.Type);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void A_missing_record_is_a_not_found_and_nothing_worse()
    {
        // A part that does not exist is an ordinary answer to an ordinary
        // question. Reporting it as a gateway failure would send somebody to
        // check a database that is working perfectly.
        ProblemDetails problem = ProblemFrom(
            Handle(new ErpRecordNotFoundException("'XXX' is not in BRANCH.")));

        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal(ErpProblemFilter.NotFoundType, problem.Type);
    }

    [Fact]
    public void A_refused_request_is_reported_as_the_system_declining()
    {
        ProblemDetails problem = ProblemFrom(
            Handle(new ErpRefusedException("SELECT is not permitted here.")));

        Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
        Assert.Equal(ErpProblemFilter.RefusedType, problem.Type);
    }

    [Fact]
    public void A_record_that_cannot_be_read_is_its_own_kind_of_failure()
    {
        // Distinct from a refusal on purpose. The database answered; what came
        // back could not be understood without guessing at its meaning, and
        // guessing is how a quantity ends up against the wrong branch.
        ProblemDetails problem = ProblemFrom(
            Handle(new MalformedRecordException("E-BRK00008 in INVENTORY: field 2 is not a number")));

        Assert.Equal(StatusCodes.Status502BadGateway, problem.Status);
        Assert.Equal(ErpProblemFilter.MalformedType, problem.Type);
    }

    [Fact]
    public void Every_failure_gets_a_type_of_its_own()
    {
        // The client switches on the type. Two failures sharing one would make
        // them indistinguishable to the screen, which is the whole problem this
        // filter exists to solve.
        string[] types =
        [
            ProblemFrom(Handle(new ErpUnreachableException("x"))).Type!,
            ProblemFrom(Handle(new ErpRecordNotFoundException("x"))).Type!,
            ProblemFrom(Handle(new ErpRefusedException("x"))).Type!,
            ProblemFrom(Handle(new MalformedRecordException("why"))).Type!,
        ];

        Assert.Equal(types.Length, types.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_detail_is_the_message_somebody_will_read_aloud()
    {
        // Someone on a call repeats this to the customer waiting, so it has to
        // survive the journey rather than being replaced with a generic line.
        ProblemDetails problem = ProblemFrom(
            Handle(new ErpUnreachableException("The stock system did not answer within five seconds.")));

        Assert.Equal("The stock system did not answer within five seconds.", problem.Detail);
    }

    [Fact]
    public void The_problem_names_the_request_it_came_from()
    {
        // So a report of a failure can be traced without asking what was open.
        ProblemDetails problem = ProblemFrom(
            Handle(new ErpRefusedException("no"), "/api/v1/parts/E-BRK00008/availability"));

        Assert.Equal("/api/v1/parts/E-BRK00008/availability", problem.Instance);
    }

    [Fact]
    public void An_exception_this_filter_does_not_know_is_left_alone()
    {
        // Anything unrecognised is a bug rather than an ERP condition, and
        // dressing it as a well-formed ERP problem would hide it behind a
        // sentence about stock data. It belongs in the logs as what it is.
        ExceptionContext context = Handle(new InvalidOperationException("a real bug"));

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Fact]
    public void The_response_is_marked_as_a_problem_document()
    {
        // The content type is how a client tells a problem from a result before
        // it has parsed either.
        ObjectResult result = Assert.IsType<ObjectResult>(
            Handle(new ErpRefusedException("no")).Result);

        Assert.Contains("application/problem+json", result.ContentTypes);
    }

    [Fact]
    public void The_status_on_the_response_matches_the_one_in_the_problem()
    {
        // Two places carry it and both are read: the HTTP status by anything in
        // between, the body's status by the client. Disagreeing would make a
        // failure look like whichever one was checked.
        ExceptionContext context = Handle(new ErpUnreachableException("x"));

        ObjectResult result = Assert.IsType<ObjectResult>(context.Result);

        Assert.Equal(ProblemFrom(context).Status, result.StatusCode);
    }
}
