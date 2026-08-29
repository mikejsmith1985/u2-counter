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

        IReadOnlyList<CustomerAccount> matches = await _customers.SearchAsync(
            q, MaximumResults, cancellationToken);

        return Ok(new CustomerSearchResponse(
            matches
                .Select(customer => new CustomerSummary(
                    customer.AccountNumber,
                    customer.Name,
                    customer.AddressLines.Count > 0 ? customer.AddressLines[0] : string.Empty,
                    customer.PriceClass))
                .ToList(),
            ResponseEnvelope.Complete()));
    }
}
