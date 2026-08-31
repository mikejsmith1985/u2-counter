namespace Counter.UnitTests.Mcp;

using System.Text.Json;
using Counter.Infrastructure.Mcp;

/// <summary>
/// Putting a record back together from the structured form the server returns.
/// </summary>
/// <remarks>
/// The MCP server hands back fields as JSON, and this puts the separators back
/// so the rest of the application works with what the ERP actually holds. It is
/// a boundary where structure is reconstructed, which makes it a place where
/// structure can quietly be lost.
///
/// Two things downstream depend on it being exact. The record view shows this
/// to a person as the stored form, and the write path compares the field
/// lengths of the record before and after a change to prove nothing moved --
/// so a rebuild that drops an empty field would compare against a record that
/// never existed and pass a write that should have failed.
/// </remarks>
public sealed class RecordRebuildingTests
{
    /// <summary>Separates fields.</summary>
    private const char AttributeMark = (char)254;

    /// <summary>Separates values within a field.</summary>
    private const char ValueMark = (char)253;

    /// <summary>Separates subvalues within a value.</summary>
    private const char SubvalueMark = (char)252;

    /// <summary>Parse a JSON literal into the element the rebuilder takes.</summary>
    /// <param name="json">The fields, keyed by their one-based position.</param>
    /// <returns>The parsed element.</returns>
    private static JsonElement Fields(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Fields_are_joined_by_position_not_by_the_order_they_arrived()
    {
        // Object key order is not guaranteed and is not the record's order. A
        // rebuild that trusted it would produce a well-formed record whose
        // every field describes something else.
        string rebuilt = ErpRecords.Rebuild(
            Fields("""{"3":"DEN","1":"Aurora","2":"AUR"}"""));

        Assert.Equal(string.Join(AttributeMark, "Aurora", "AUR", "DEN"), rebuilt);
    }

    [Fact]
    public void A_gap_in_the_positions_becomes_an_empty_field()
    {
        // The one that matters most. Skipping position two would shift every
        // field after it up by one -- the same silent corruption the parser
        // guards against at the other end -- and nothing would report it.
        string rebuilt = ErpRecords.Rebuild(
            Fields("""{"1":"Aurora","4":"DEN"}"""));

        Assert.Equal(
            string.Join(AttributeMark, "Aurora", string.Empty, string.Empty, "DEN"),
            rebuilt);
    }

    [Fact]
    public void Multivalued_fields_come_back_separated_by_value_marks()
    {
        string rebuilt = ErpRecords.Rebuild(
            Fields("""{"1":["LKW","GRE","DEN"],"2":["16","16","14"]}"""));

        Assert.Equal(
            string.Join(
                AttributeMark,
                string.Join(ValueMark, "LKW", "GRE", "DEN"),
                string.Join(ValueMark, "16", "16", "14")),
            rebuilt);
    }

    [Fact]
    public void Subvalues_survive_the_journey()
    {
        // Two telephone numbers belonging to one contact. Flattening them would
        // turn one contact with two numbers into two contacts, and the field
        // beside it would then be read against the wrong person.
        string rebuilt = ErpRecords.Rebuild(
            Fields("""{"1":[["303-555-0100","720-555-0100"],["303-555-0200"]]}"""));

        Assert.Equal(
            string.Join(
                ValueMark,
                string.Join(SubvalueMark, "303-555-0100", "720-555-0100"),
                "303-555-0200"),
            rebuilt);
    }

    [Fact]
    public void An_empty_value_inside_a_field_keeps_its_place()
    {
        // A bin location nobody recorded. Dropping it shortens the field, and a
        // field one value short against its parallel siblings is exactly how a
        // quantity ends up against the wrong branch.
        string rebuilt = ErpRecords.Rebuild(
            Fields("""{"1":["A-31","","C-22"]}"""));

        Assert.Equal(string.Join(ValueMark, "A-31", string.Empty, "C-22"), rebuilt);
    }

    [Fact]
    public void A_record_with_no_fields_is_empty_rather_than_a_lone_separator()
    {
        Assert.Equal(string.Empty, ErpRecords.Rebuild(Fields("{}")));
    }

    [Fact]
    public void Anything_that_is_not_an_object_of_fields_is_empty()
    {
        // The server reporting an error returns something else entirely, and
        // rebuilding that into a plausible-looking record would be worse than
        // returning nothing.
        Assert.Equal(string.Empty, ErpRecords.Rebuild(Fields("[]")));
        Assert.Equal(string.Empty, ErpRecords.Rebuild(Fields("\"not a record\"")));
    }

    [Fact]
    public void Keys_that_are_not_positions_are_ignored()
    {
        // The payload carries more than fields. A key like "id" is not field
        // zero and must not become one.
        string rebuilt = ErpRecords.Rebuild(
            Fields("""{"id":"AUR","1":"Aurora","2":"AUR"}"""));

        Assert.Equal(string.Join(AttributeMark, "Aurora", "AUR"), rebuilt);
    }

    [Fact]
    public void Record_ids_are_read_from_a_select_list()
    {
        IReadOnlyList<string> ids = ErpRecords.RecordIdsFrom(
            Fields("""{"record_ids":["AUR","DEN","GRJ"]}"""));

        Assert.Equal(["AUR", "DEN", "GRJ"], ids);
    }

    [Fact]
    public void Blank_ids_are_dropped_rather_than_returned_as_keys()
    {
        // A blank key would be read back as a record that does not exist, and
        // reported as a part the account does not hold.
        IReadOnlyList<string> ids = ErpRecords.RecordIdsFrom(
            Fields("""{"record_ids":["AUR","","DEN"]}"""));

        Assert.Equal(["AUR", "DEN"], ids);
    }

    [Fact]
    public void A_payload_with_no_record_ids_is_no_records_rather_than_a_failure()
    {
        // A select matching nothing is an ordinary answer, not an error.
        Assert.Empty(ErpRecords.RecordIdsFrom(Fields("""{"count":0}""")));
        Assert.Empty(ErpRecords.RecordIdsFrom(Fields("""{"record_ids":"AUR"}""")));
    }
}
