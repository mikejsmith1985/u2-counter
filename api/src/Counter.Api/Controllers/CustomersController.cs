namespace Counter.Api.Controllers;

using Counter.Api.Contracts;
using Counter.Infrastructure.Erp;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Finding the customer being served, so their price can be quoted.
/// </summary>
[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(CustomerDirectory customers) : ControllerBase
{
    /// <summary>Most matches to return, however many are asked for.</summary>
    private const int MaximumResults = 15;

    private readonly CustomerDirectory _customers = customers;

    /// <summary>
    /// List the account file, for a representative who has not been told who
    /// they are serving.
    /// </summary>
    /// <param name="limit">Most customers to return.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <response code="200">The first customers, and how many there are altogether.</response>
    /// <remarks>
    /// A selector that shows nothing until two characters are typed cannot be used
    /// by anyone who does not already know a customer's name -- which is everyone
    /// meeting the system for the first time, including everybody it gets
    /// demonstrated to.
    /// </remarks>
    [HttpGet("browse")]
    [ProducesResponseType<BrowseResponse<CustomerSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrowseResponse<CustomerSummary>>> Browse(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        int effectiveLimit = Math.Clamp(limit ?? MaximumResults, 1, MaximumResults);

        (IReadOnlyList<CustomerAccount> page, int total) =
            await _customers.BrowseAsync(effectiveLimit, cancellationToken);

        return Ok(new BrowseResponse<CustomerSummary>(
            page.Select(ToSummary).ToList(), total, ResponseEnvelope.Complete()));
    }

    /// <summary>
    /// Find customers by account number or name.
    /// </summary>
    /// <param name="q">What the user typed.</param>
    /// <param name="cancellationToken">Abandons the search when the caller gives up.</param>
    /// <response code="200">Matches, which may legitimately be none.</response>
    [HttpGet]
    [ProducesResponseType<CustomerSearchResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerSearchResponse>> Search(
        [FromQuery] string q,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(new CustomerSearchResponse([], ResponseEnvelope.Complete()));
        }

        // One more than will be shown, so a full page can be told from a page
        // that happens to end there. Asking for exactly the limit gives an answer
        // that is either complete or truncated, with no way to know which.
        IReadOnlyList<CustomerAccount> matches = await _customers.SearchAsync(
            q, MaximumResults + 1, cancellationToken);

        bool wasCapped = matches.Count > MaximumResults;

        return Ok(new CustomerSearchResponse(
            matches
                .Take(MaximumResults)
                .Select(ToSummary)
                .ToList(),
            // A customer picking the wrong account because theirs was the
            // sixteenth match is a quote given against somebody else's contract.
            wasCapped
                ? ResponseEnvelope.Partial(
                    $"More than {MaximumResults} customers match. Type more of the name.")
                : ResponseEnvelope.Complete()));
    }

    /// <summary>
    /// Reduce an account to what a picker row shows.
    /// </summary>
    /// <param name="customer">The account as the ERP holds it.</param>
    /// <returns>The row shown in the selector.</returns>
    /// <remarks>
    /// Shared by searching and browsing so the two cannot drift. A selector whose
    /// rows carried different fields depending on whether the representative had
    /// typed anything would be one where the price class appeared and disappeared
    /// -- and the price class is the whole reason for choosing a customer.
    /// </remarks>
    private static CustomerSummary ToSummary(CustomerAccount customer) => new(
        customer.AccountNumber,
        customer.Name,
        customer.AddressLines.Count > 0 ? customer.AddressLines[0] : string.Empty,
        customer.PriceClass);
}
