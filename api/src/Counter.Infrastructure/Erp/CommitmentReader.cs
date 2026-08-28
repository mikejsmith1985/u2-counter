namespace Counter.Infrastructure.Erp;

using Counter.Domain.Orders;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads the orders holding stock for a part at a branch.
/// </summary>
public sealed class CommitmentReader(
    IErpReader erp,
    PricingReader customers,
    ILogger<CommitmentReader> logger)
{
    /// <summary>Most orders to consider for one part.</summary>
    private const int MaximumOrders = 500;

    private readonly IErpReader _erp = erp;
    private readonly PricingReader _customers = customers;
    private readonly ILogger<CommitmentReader> _logger = logger;

    /// <summary>
    /// Find what is holding a branch's committed stock.
    /// </summary>
    /// <param name="partNumber">The part, already validated against the catalogue.</param>
    /// <param name="branchCode">The branch, already validated against BRANCH.</param>
    /// <param name="committedTotal">What the inventory record says is committed.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <remarks>
    /// The state filter is applied while reading rather than afterwards, so the
    /// rule about which states hold stock lives in one place. Filtering in two
    /// places would leave two chances to get it wrong and no test that they agree.
    /// </remarks>
    public async Task<BranchCommitments> ReadAsync(
        string partNumber,
        string branchCode,
        int committedTotal,
        CancellationToken cancellationToken)
    {
        // Selected by part and by state in the query itself. Reading every order
        // and filtering here would mean hundreds of round trips for one lookup,
        // and would put the rule about which states hold stock in a second place.
        string query =
            $"SELECT {ErpFiles.Order.Name} " +
            $"WITH F{ErpFiles.Order.LineParts} = \"{partNumber}\" " +
            $"AND WITH F{ErpFiles.Order.State} = {StatesHoldingStockClause()}";

        IReadOnlyList<string> orderNumbers = await _erp.SelectKeysAsync(
            query, MaximumOrders, cancellationToken);

        List<Commitment> holding = [];

        foreach (string orderNumber in orderNumbers)
        {
            Commitment? commitment = await ReadCommitmentAsync(
                orderNumber, partNumber, branchCode, cancellationToken);

            if (commitment is not null)
            {
                holding.Add(commitment);
            }
        }

        return new BranchCommitments(
            branchCode,
            committedTotal,
            holding.OrderBy(commitment => commitment.PromisedDate).ToList());
    }

    /// <summary>
    /// The states that hold stock, quoted for a selection clause.
    /// </summary>
    /// <remarks>
    /// Built from the domain's own set rather than written out, so adding a state
    /// there cannot leave this query behind.
    /// </remarks>
    private static string StatesHoldingStockClause() =>
        string.Join(
            ' ',
            OrderStates.HoldingStock
                .Select(state => $"\"{state.ToString().ToUpperInvariant()}\"")
                .OrderBy(quoted => quoted, StringComparer.Ordinal));

    /// <summary>
    /// Read one order, returning what it holds of this part at this branch.
    /// </summary>
    /// <returns>The commitment, or null when this order holds nothing relevant.</returns>
    private async Task<Commitment?> ReadCommitmentAsync(
        string orderNumber,
        string partNumber,
        string branchCode,
        CancellationToken cancellationToken)
    {
        string raw;
        try
        {
            raw = await _erp.ReadRecordAsync(ErpFiles.Order.Name, orderNumber, cancellationToken);
        }
        catch (ErpRecordNotFoundException)
        {
            return null;
        }

        MultiValueRecord record = MultiValueRecord.Parse(raw);
        OrderState state = OrderStates.Parse(record.Field(ErpFiles.Order.State));

        if (!state.HoldsStock())
        {
            if (state == OrderState.Unrecognised)
            {
                // Worth recording: an unrecognised state is data nobody has
                // noticed drifting, and it is being deliberately excluded.
                _logger.LogWarning(
                    "Order {OrderNumber} carries an unrecognised state '{State}'; it is treated " +
                    "as holding no stock",
                    orderNumber,
                    record.Field(ErpFiles.Order.State));
            }

            return null;
        }

        int quantity = QuantityFor(record, partNumber, branchCode);
        if (quantity == 0)
        {
            return null;
        }

        string account = record.Field(ErpFiles.Order.CustomerAccount);

        return new Commitment(
            OrderNumber: orderNumber,
            CustomerAccount: account,
            CustomerName: await ReadCustomerNameAsync(account, cancellationToken),
            Quantity: quantity,
            State: state,
            PromisedDate: PromisedDateFor(record, partNumber, branchCode));
    }

    /// <summary>
    /// Total quantity this order holds of one part at one branch.
    /// </summary>
    /// <remarks>
    /// Line fields are parallel, so a line is only counted when the part and the
    /// branch at the same position both match. Matching them independently would
    /// count a line for the right part at the wrong branch.
    /// </remarks>
    private static int QuantityFor(MultiValueRecord record, string partNumber, string branchCode)
    {
        IReadOnlyList<string> parts = record.Values(ErpFiles.Order.LineParts);
        IReadOnlyList<string> quantities = record.Values(ErpFiles.Order.LineQuantities);
        IReadOnlyList<string> branches = record.Values(ErpFiles.Order.LineBranches);

        int total = 0;

        for (int line = 0; line < parts.Count; line++)
        {
            bool isThisPart = string.Equals(
                parts[line], partNumber, StringComparison.OrdinalIgnoreCase);

            bool isThisBranch = line < branches.Count && string.Equals(
                branches[line], branchCode, StringComparison.OrdinalIgnoreCase);

            if (isThisPart && isThisBranch && line < quantities.Count &&
                int.TryParse(quantities[line], out int quantity))
            {
                total += quantity;
            }
        }

        return total;
    }

    /// <summary>The promised date of the first matching line.</summary>
    private static DateOnly PromisedDateFor(
        MultiValueRecord record,
        string partNumber,
        string branchCode)
    {
        IReadOnlyList<string> parts = record.Values(ErpFiles.Order.LineParts);
        IReadOnlyList<string> branches = record.Values(ErpFiles.Order.LineBranches);
        IReadOnlyList<string> dates = record.Values(ErpFiles.Order.LinePromisedDates);

        for (int line = 0; line < parts.Count; line++)
        {
            bool matches = string.Equals(parts[line], partNumber, StringComparison.OrdinalIgnoreCase)
                && line < branches.Count
                && string.Equals(branches[line], branchCode, StringComparison.OrdinalIgnoreCase);

            if (matches && line < dates.Count && DateOnly.TryParse(dates[line], out DateOnly date))
            {
                return date;
            }
        }

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>Read a customer's name, falling back to the account number.</summary>
    private async Task<string> ReadCustomerNameAsync(
        string account,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return "Unknown customer";
        }

        try
        {
            CustomerAccount customer = await _customers.ReadCustomerAsync(
                account, cancellationToken);

            return customer.Name;
        }
        catch (ErpRecordNotFoundException)
        {
            // A commitment against an account nobody can find is still a
            // commitment. Showing the number is more useful than hiding the line.
            return account;
        }
    }

}
