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
                .Select(customer => new CustomerSummary(
                    customer.AccountNumber,
                    customer.Name,
                    customer.AddressLines.Count > 0 ? customer.AddressLines[0] : string.Empty,
                    customer.PriceClass))
                .ToList(),
            // A customer picking the wrong account because theirs was the
            // sixteenth match is a quote given against somebody else's contract.
            wasCapped
                ? ResponseEnvelope.Partial(
                    $"More than {MaximumResults} customers match. Type more of the name.")
                : ResponseEnvelope.Complete()));
    }
}
