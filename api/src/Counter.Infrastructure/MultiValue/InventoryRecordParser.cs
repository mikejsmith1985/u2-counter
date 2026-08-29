namespace Counter.Infrastructure.MultiValue;

using Counter.Domain.Availability;
using Counter.Infrastructure.Erp;

/// <summary>
/// Turns an inventory record's parallel fields into branch positions.
/// </summary>
/// <remarks>
/// This is the most safety-critical parsing in the application, and the reason is
/// that its failure mode is invisible. If a branch is paired with another
/// branch's quantities, every number on the screen is a real number and the
/// screen looks entirely correct — nothing downstream can detect it, and a
/// representative would confidently tell a customer something untrue.
///
/// Three things guard it. Each position is built in one step, so the fields are
/// never carried forward as separate lists that could be combined wrongly. A
/// short field is padded rather than truncating the others, so a missing bin
/// cannot shift the branch it belongs to. And a field longer than the branch list
/// is rejected, because a quantity for a branch nobody named cannot be attributed
/// without inventing a fact.
/// </remarks>
public static class InventoryRecordParser
{
    // Taken from ErpFiles rather than restated. The same number in two places
    // is two opportunities for one of them to drift, and this is the layout where
    // drifting produces a plausible wrong answer rather than an obvious one.
    private const int BranchField = ErpFiles.Inventory.BranchCodes;
    private const int OnHandField = ErpFiles.Inventory.OnHand;
    private const int CommittedField = ErpFiles.Inventory.Committed;
    private const int OnOrderField = ErpFiles.Inventory.OnOrder;
    private const int BinField = ErpFiles.Inventory.Bin;

    /// <summary>
    /// Read every branch position from an inventory record.
    /// </summary>
    /// <param name="raw">The record as the ERP stores it.</param>
    /// <returns>One position per branch named in the record.</returns>
    /// <exception cref="MalformedRecordException">
    /// When a quantity exists for a branch the record never names.
    /// </exception>
    public static IReadOnlyList<BranchPosition> Parse(string raw)
    {
        MultiValueRecord record = MultiValueRecord.Parse(raw);

        IReadOnlyList<string> branches = record.Values(BranchField);
        if (branches.Count == 0)
        {
            return [];
        }

        RejectFieldsLongerThanTheBranchList(record, branches.Count);

        IReadOnlyList<string> onHand = record.Values(OnHandField);
        IReadOnlyList<string> committed = record.Values(CommittedField);
        IReadOnlyList<string> onOrder = record.Values(OnOrderField);
        IReadOnlyList<string> bins = record.Values(BinField);

        List<BranchPosition> positions = new(branches.Count);

        for (int position = 0; position < branches.Count; position++)
        {
            // Built in one step, from one index, so nothing can be mismatched
            // between fields.
            positions.Add(new BranchPosition(
                BranchCode: branches[position],
                OnHand: WholeNumberAt(onHand, position),
                Committed: WholeNumberAt(committed, position),
                OnOrder: WholeNumberAt(onOrder, position),
                Bin: TextAt(bins, position)));
        }

        return positions;
    }

    /// <summary>
    /// Refuse a record holding a quantity for a branch it never names.
    /// </summary>
    private static void RejectFieldsLongerThanTheBranchList(
        MultiValueRecord record,
        int branchCount)
    {
        foreach (int fieldNumber in new[] { OnHandField, CommittedField, OnOrderField, BinField })
        {
            int valueCount = record.Values(fieldNumber).Count;

            if (valueCount > branchCount)
            {
                throw new MalformedRecordException(
                    $"Field {fieldNumber} holds {valueCount} values where field {BranchField} " +
                    $"names {branchCount} branches. A quantity with no branch cannot be " +
                    "attributed to one.");
            }
        }
    }

    /// <summary>
    /// Read a quantity at a position, treating a missing or blank one as zero.
    /// </summary>
    /// <remarks>
    /// Padding rather than failing: a record whose bin field is short simply means
    /// one branch has no bin recorded, which is ordinary data. Shortening the
    /// other fields to match would drop a branch entirely.
    /// </remarks>
    private static int WholeNumberAt(IReadOnlyList<string> values, int position)
    {
        if (position >= values.Count)
        {
            return 0;
        }

        return int.TryParse(values[position], out int quantity) ? quantity : 0;
    }

    /// <summary>Read text at a position, treating a missing one as empty.</summary>
    private static string TextAt(IReadOnlyList<string> values, int position) =>
        position < values.Count ? values[position] : string.Empty;
}

/// <summary>
/// Raised when a record cannot be read without guessing at what it means.
/// </summary>
/// <param name="message">What is wrong with the record.</param>
public sealed class MalformedRecordException(string message) : Exception(message);
