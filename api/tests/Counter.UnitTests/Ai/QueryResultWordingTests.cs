namespace Counter.UnitTests.Ai;

using Counter.Domain.Catalogue;
using Counter.Infrastructure.Ai;

/// <summary>
/// What selected records say to the model that reads them.
/// </summary>
/// <remarks>
/// This formatter has the worst record in the project. It used to hand each
/// record over as a run of separators and let the model count positions --
/// the exact task this whole application exists to say is dangerous. Asked
/// which parts had committed stock, it counted wrong in the obvious way: read
/// field four, ON.ORDER, as field three, COMMITTED, and reported three
/// branches holding stock that had none. It cited the record it had misread,
/// which makes it worse than a refusal, because the working looked sound.
///
/// Naming each field from the file's dictionary is what removed the counting,
/// so these tests are about labelling and about position -- not about whether
/// the query ran.
/// </remarks>
public sealed class QueryResultWordingTests
{
    /// <summary>Separates fields.</summary>
    private const char AttributeMark = (char)254;

    /// <summary>Separates values within a field.</summary>
    private const char ValueMark = (char)253;

    /// <summary>A dictionary entry.</summary>
    /// <param name="position">Which field it describes.</param>
    /// <param name="name">Its name in the dictionary.</param>
    /// <param name="isMultiValued">Whether it holds many values.</param>
    /// <returns>The field.</returns>
    private static DictionaryField Field(int position, string name, bool isMultiValued = false) =>
        new(name, position, name, string.Empty, isMultiValued, string.Empty);

    /// <summary>The INVENTORY dictionary, as far as these tests need it.</summary>
    /// <returns>The fields, in position order.</returns>
    private static IReadOnlyList<DictionaryField> InventoryDictionary() =>
    [
        Field(1, "BRANCH", isMultiValued: true),
        Field(2, "ON.HAND", isMultiValued: true),
        Field(3, "COMMITTED", isMultiValued: true),
        Field(4, "ON.ORDER", isMultiValued: true),
    ];

    /// <summary>The record from the answer that was wrong.</summary>
    /// <returns>Branches, on hand, committed, on order.</returns>
    private static string TheRecordThatWasMisread() =>
        string.Join(
            AttributeMark,
            string.Join(ValueMark, "LKW", "GRE", "FTC"),
            string.Join(ValueMark, "16", "16", "16"),
            string.Join(ValueMark, "0", "0", "0"),
            string.Join(ValueMark, "266", "210", "230"));

    [Fact]
    public void Every_field_is_named_so_nothing_has_to_be_counted()
    {
        // The fix for the defect. Counting positions across separators is the
        // task that produced three branches of stock that did not exist.
        string described = AskService.DescribeRecords(
            ["E-BRK00008"],
            new Dictionary<string, string> { ["E-BRK00008"] = TheRecordThatWasMisread() },
            InventoryDictionary());

        Assert.Contains("COMMITTED", described, StringComparison.Ordinal);
        Assert.Contains("ON.ORDER", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_committed_field_carries_the_committed_figures_and_not_the_on_order_ones()
    {
        // The specific misreading, pinned. Committed is all zeroes here and
        // on order is in the hundreds, so a formatter that swapped them would
        // be caught by the numbers rather than by the label alone.
        string described = AskService.DescribeRecords(
            ["E-BRK00008"],
            new Dictionary<string, string> { ["E-BRK00008"] = TheRecordThatWasMisread() },
            InventoryDictionary());

        Assert.Contains("3 COMMITTED: 0, 0, 0", described, StringComparison.Ordinal);
        Assert.Contains("4 ON.ORDER: 266, 210, 230", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_field_is_numbered_as_well_as_named()
    {
        // So an answer can be checked against the record by hand.
        string described = AskService.DescribeRecords(
            ["E-BRK00008"],
            new Dictionary<string, string> { ["E-BRK00008"] = TheRecordThatWasMisread() },
            InventoryDictionary());

        Assert.Contains("1 BRANCH: LKW, GRE, FTC", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rule_about_reading_across_positions_is_stated_every_time()
    {
        // Parallel fields are the one thing about this data a reader has to be
        // told. Saying it once in a system prompt is not the same as saying it
        // beside the record it applies to.
        string described = AskService.DescribeRecords(
            ["E-BRK00008"],
            new Dictionary<string, string> { ["E-BRK00008"] = TheRecordThatWasMisread() },
            InventoryDictionary());

        Assert.Contains("position n of every multi-valued field", described, StringComparison.Ordinal);
        Assert.Contains("do not count separators", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_the_dictionary_does_not_describe_is_numbered_rather_than_dropped()
    {
        // A record longer than its dictionary is ordinary in a system that has
        // been running for decades. Dropping the field would hide data; naming
        // it something invented would be worse.
        string described = AskService.DescribeRecords(
            ["E-BRK00008"],
            new Dictionary<string, string>
            {
                ["E-BRK00008"] = string.Join(AttributeMark, "LKW", "16", "0", "266", "extra"),
            },
            InventoryDictionary());

        Assert.Contains("5 field 5: extra", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_valued_field_is_shown_without_being_split()
    {
        // Turning one value into a list of one changes what the model reads
        // about the field's shape.
        string described = AskService.DescribeRecords(
            ["AUR"],
            new Dictionary<string, string> { ["AUR"] = string.Join(AttributeMark, "Aurora", "DEN") },
            [Field(1, "NAME"), Field(2, "REGION")]);

        Assert.Contains("1 NAME: Aurora", described, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurora,", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_keys_that_matched_are_listed_before_any_record()
    {
        // The selection is the answer to "which ones", and it is often the
        // whole answer -- the records are the evidence for it.
        string described = AskService.DescribeRecords(
            ["AUR", "DEN", "GRJ"],
            new Dictionary<string, string> { ["AUR"] = "Aurora" },
            [Field(1, "NAME")]);

        Assert.StartsWith("3 key(s): AUR, DEN, GRJ", described, StringComparison.Ordinal);
    }

    [Fact]
    public void More_keys_than_records_read_is_not_hidden()
    {
        // A selection can match more than is read back. The key list says how
        // many matched, so an answer built only on the records shown can be
        // seen to be built on a subset.
        string described = AskService.DescribeRecords(
            ["AUR", "DEN", "GRJ", "LKW"],
            new Dictionary<string, string> { ["AUR"] = "Aurora" },
            [Field(1, "NAME")]);

        Assert.Contains("4 key(s)", described, StringComparison.Ordinal);
        Assert.Contains("AUR:", described, StringComparison.Ordinal);
        Assert.DoesNotContain("DEN:", described, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_value_inside_a_field_keeps_its_position()
    {
        // A bin nobody recorded. Dropping it shortens the list and every
        // position after it describes the wrong branch -- which is the whole
        // failure this formatter exists to prevent.
        string described = AskService.DescribeRecords(
            ["E-BRK00008"],
            new Dictionary<string, string>
            {
                ["E-BRK00008"] = string.Join(ValueMark, "A-31", string.Empty, "C-22"),
            },
            [Field(1, "BIN", isMultiValued: true)]);

        Assert.Contains("1 BIN: A-31, , C-22", described, StringComparison.Ordinal);
    }
}
