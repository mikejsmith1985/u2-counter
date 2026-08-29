namespace Counter.Infrastructure.Erp;

using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.Logging;

/// <summary>
/// The customer list, held in memory so the selector answers as fast as search.
/// </summary>
/// <remarks>
/// Customers are reference data in the same sense as the catalogue: who exists
/// changes rarely, and a stale entry is at worst an account that has closed.
/// Their <em>prices</em> are read live every time, because a stale price quoted
/// on a call is a commitment nobody agreed to.
/// </remarks>
public sealed class CustomerDirectory(IErpReader erp, ILogger<CustomerDirectory> logger)
{
    /// <summary>Most accounts to load.</summary>
    private const int MaximumCustomers = 10_000;

    /// <summary>Records read per call.</summary>
    private const int BatchSize = 250;

    private readonly IErpReader _erp = erp;
    private readonly ILogger<CustomerDirectory> _logger = logger;
    private readonly SemaphoreSlim _buildGate = new(1, 1);

    private IReadOnlyList<CustomerAccount> _customers = [];

    /// <summary>
    /// Find customers by account number or name.
    /// </summary>
    /// <param name="text">What the user typed.</param>
    /// <param name="limit">Most matches to return.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    public async Task<IReadOnlyList<CustomerAccount>> SearchAsync(
        string text,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureBuiltAsync(cancellationToken);

        string needle = text.Trim();

        return _customers
            .Where(customer =>
                customer.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                customer.AccountNumber.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(customer => customer.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Build the directory if it is not already built, retrying after a failure.
    /// </summary>
    /// <param name="cancellationToken">Abandons the build when the caller gives up.</param>
    public async Task EnsureBuiltAsync(CancellationToken cancellationToken)
    {
        if (_customers.Count > 0)
        {
            return;
        }

        await _buildGate.WaitAsync(cancellationToken);
        try
        {
            if (_customers.Count > 0)
            {
                return;
            }

            IReadOnlyList<string> keys = await _erp.SelectKeysAsync(
                $"SELECT {ErpFiles.Customer.Name}", MaximumCustomers, cancellationToken);

            List<CustomerAccount> accounts = new(keys.Count);

            foreach (string[] batch in keys.Chunk(BatchSize))
            {
                IReadOnlyDictionary<string, string> records =
                    await _erp.ReadRecordsAsync(ErpFiles.Customer.Name, batch, cancellationToken);

                accounts.AddRange(records.Select(record =>
                    PricingReader.ReadCustomer(record.Key, record.Value)));
            }

            _customers = accounts;
            _logger.LogInformation("Customer directory built with {Count} accounts", accounts.Count);
        }
        finally
        {
            _buildGate.Release();
        }
    }
}
