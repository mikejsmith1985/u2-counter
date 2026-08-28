namespace Counter.UnitTests;

using Counter.Domain.Availability;
using Counter.Infrastructure.MultiValue;

/// <summary>
/// Reading parallel fields into branch positions.
/// </summary>
/// <remarks>
/// The alignment tests carry the most weight here. Their failure mode is
/// invisible: a branch paired with another branch's quantities produces a screen
/// where every number is real and the pairing is wrong, which nothing downstream
/// can detect.
/// </remarks>
public sealed class MultiValueParserTests
{
    private const char AM = (char)254;
    private const char VM = (char)253;
    private const char SM = (char)252;

    /// <summary>Build a record from fields, joining values with value marks.</summary>
    private static string Record(params string[][] fields) =>
        string.Join(AM, fields.Select(values => string.Join(VM, values)));

    public sealed class Alignment
    {
        [Fact]
        public void Each_branch_keeps_its_own_quantities()
        {
            string raw = Record(
                ["DEN", "AUR", "BOU"],
                ["142", "38", "0"],
                ["40", "12", "0"],
                ["0", "0", "200"],
                ["A-12", "C-04", ""]);

            IReadOnlyList<BranchPosition> positions = InventoryRecordParser.Parse(raw);

            Assert.Equal("AUR", positions[1].BranchCode);
            Assert.Equal(38, positions[1].OnHand);
            Assert.Equal(12, positions[1].Committed);
        }

        [Fact]
        public void A_short_field_does_not_shift_the_branch_it_belongs_to()
        {
            // The bin field is one short. Boulder must still be Boulder, with its
            // own quantities, and simply have no bin.
            string raw = Record(
                ["DEN", "AUR", "BOU"],
                ["142", "38", "7"],
                ["40", "12", "0"],
                ["0", "0", "0"],
                ["A-12", "C-04"]);

            IReadOnlyList<BranchPosition> positions = InventoryRecordParser.Parse(raw);

            Assert.Equal("BOU", positions[2].BranchCode);
            Assert.Equal(7, positions[2].OnHand);
            Assert.Equal(string.Empty, positions[2].Bin);
        }

        [Fact]
        public void A_short_field_does_not_shorten_the_others()
        {
            string raw = Record(["DEN", "AUR", "BOU"], ["1", "2", "3"], ["0", "0"]);

            Assert.Equal(3, InventoryRecordParser.Parse(raw).Count);
        }

        [Fact]
        public void A_quantity_for_an_unnamed_branch_is_refused()
        {
            // There is no branch to attribute it to, and guessing would invent a
            // fact. Refusing is the only honest option.
            string raw = Record(["DEN", "AUR"], ["142", "38", "99"]);

            Assert.Throws<MalformedRecordException>(() => InventoryRecordParser.Parse(raw));
        }

        [Fact]
        public void The_refusal_names_the_offending_field()
        {
            string raw = Record(["DEN", "AUR"], ["1", "2"], ["0", "0", "0"]);

            MalformedRecordException error = Assert.Throws<MalformedRecordException>(
                () => InventoryRecordParser.Parse(raw));

            Assert.Contains("Field 3", error.Message);
        }
    }

    public sealed class EdgeCases
    {
        [Fact]
        public void An_empty_record_yields_no_positions()
        {
            Assert.Empty(InventoryRecordParser.Parse(string.Empty));
        }

        [Fact]
        public void A_record_with_one_branch_yields_one_position()
        {
            string raw = Record(["DEN"], ["500"], ["0"], ["0"], ["A-01"]);

            Assert.Single(InventoryRecordParser.Parse(raw));
        }

        [Fact]
        public void A_branch_with_no_quantities_at_all_reads_as_zero()
        {
            // A record naming a branch but stopping there is ordinary data:
            // MultiValue does not store trailing empty fields.
            IReadOnlyList<BranchPosition> positions = InventoryRecordParser.Parse("DEN");

            Assert.Equal(0, positions[0].OnHand);
            Assert.Equal(0, positions[0].Committed);
        }

        [Fact]
        public void A_non_numeric_quantity_reads_as_zero_rather_than_failing()
        {
            // One unreadable quantity should not make a whole part unavailable to
            // look up. Zero is the safe reading: it never overstates stock.
            string raw = Record(["DEN"], ["lots"]);

            Assert.Equal(0, InventoryRecordParser.Parse(raw)[0].OnHand);
        }

        [Fact]
        public void An_empty_interior_value_reads_as_zero_without_shifting_others()
        {
            string raw = Record(["DEN", "AUR", "BOU"], ["10", "", "30"]);

            IReadOnlyList<BranchPosition> positions = InventoryRecordParser.Parse(raw);

            Assert.Equal(0, positions[1].OnHand);
            Assert.Equal(30, positions[2].OnHand);
        }
    }

    public sealed class RecordReading
    {
        [Fact]
        public void Fields_are_numbered_from_one()
        {
            // Matching how every person working with these systems refers to them.
            MultiValueRecord record = MultiValueRecord.Parse(Record(["first"], ["second"]));

            Assert.Equal("first", record.Field(1));
            Assert.Equal("second", record.Field(2));
        }

        [Fact]
        public void A_field_beyond_the_end_is_empty_rather_than_an_error()
        {
            MultiValueRecord record = MultiValueRecord.Parse("only");

            Assert.Equal(string.Empty, record.Field(9));
        }

        [Fact]
        public void Subvalues_are_read_from_within_a_value()
        {
            string raw = "Ray" + VM + "Marta" + AM + "303-0188" + SM + "720-0913" + VM + "303-0190";

            MultiValueRecord record = MultiValueRecord.Parse(raw);

            Assert.Equal(["303-0188", "720-0913"], record.Subvalues(2, 1));
        }

        [Fact]
        public void The_raw_record_is_preserved_exactly()
        {
            // The record view shows this to a user, so nothing may be normalised
            // away on the journey.
            string raw = Record(["DEN", "AUR"], ["1", "2"]);

            Assert.Equal(raw, MultiValueRecord.Parse(raw).Raw);
        }

        [Fact]
        public void Text_in_other_alphabets_survives_parsing()
        {
            string raw = Record(["MÜLLER GmbH"], ["£1,250.00"]);

            MultiValueRecord record = MultiValueRecord.Parse(raw);

            Assert.Equal("MÜLLER GmbH", record.Field(1));
            Assert.Equal("£1,250.00", record.Field(2));
        }
    }
}
