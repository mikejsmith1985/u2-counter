namespace Counter.Api.Filters;

using Counter.Infrastructure.Mcp;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

/// <summary>
/// Gives every request a hard budget, and stops the work when it runs out.
/// </summary>
/// <remarks>
/// The distinction that makes this worth a filter: a timeout that only stops the
/// waiting leaves the query running. Under load that is how a system dies —
/// callers give up, the queries they abandoned keep their connections, and the
/// next caller waits behind work nobody is going to read.
///
/// So the budget is a linked cancellation token replacing the request's own. It
/// reaches the reader, and through it the MCP server, which abandons the command
/// and drops the session rather than waiting for an answer no longer wanted.
///
/// The reader applies the same budget to its own call. That is deliberate
/// duplication: this one protects the person waiting, the reader's protects the
/// database from an abandoned query, and either alone leaves the other's gap.
/// </remarks>
public sealed class ErpTimeoutFilter(IOptions<ErpConnectionOptions> options) : IAsyncActionFilter
{
    private readonly TimeSpan _budget = options.Value.RequestBudget;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        CancellationToken clientToken = context.HttpContext.RequestAborted;

        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(clientToken);
        budget.CancelAfter(_budget);

        // Replacing RequestAborted is what makes the budget reach the work.
        // Anything the action awaits takes its token from here, so cancelling
        // this source stops the query rather than only the wait for it.
        context.HttpContext.RequestAborted = budget.Token;

        try
        {
            await next();
        }
        catch (OperationCanceledException) when (!clientToken.IsCancellationRequested)
        {
            // The budget ran out, not the client leaving. Reported as unreachable
            // so it arrives as the same answer as any other failure to reach the
            // data, and never as an empty result.
            throw new ErpUnreachableException(
                $"The ERP did not answer within {_budget.TotalSeconds:0} seconds.");
        }
        finally
        {
            context.HttpContext.RequestAborted = clientToken;
        }
    }
}
