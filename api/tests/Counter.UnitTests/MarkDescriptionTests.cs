namespace Counter.UnitTests;

using Counter.Infrastructure.MultiValue;

/// <summary>
/// Every separator a record holds is described, and none that it does not.
/// </summary>
/// <remarks>
/// The record drawer is the screen that has to survive an ERP developer looking
/// at it. Its whole claim is that the record beside the parsed form is the record
/// as stored — separators included, nothing tidied away. A mark present but not
/// described appears on screen as an unexplained gap; a mark described but not
/// present labels a structure the record does not have. Both are the drawer
/// quietly telling a story about the data rather than showing it.
/// </remarks>
public sealed class MarkDescriptionTests
{
    private const char AttributeMark = (char)254;
    private const char ValueMark = (char)253;
    private const char SubvalueMark = (char)252;

    [Fact]
    public void A_record_with_only_fields_describes_only_the_attribute_mark()
    {
        IReadOnlyList<RecordMark> marks = RecordMarks.PresentIn($"DEN{AttributeMark}60");

        Assert.Equal("Attribute mark", Assert.Single(marks).Name);
    }

    [Fact]
    public void A_record_with_multiple_values_describes_the_value_mark_too()
    {
        string raw = $"DEN{ValueMark}AUR{AttributeMark}60{ValueMark}10";

        IReadOnlyList<RecordMark> marks = RecordMarks.PresentIn(raw);

        Assert.Equal(2, marks.Count);
        Assert.Contains(marks, mark => mark.Code == 254);
        Assert.Contains(marks, mark => mark.Code == 253);
    }

    [Fact]
    public void A_record_with_subvalues_describes_all_three()
    {
        // The customer record is the only three-level structure in the data, and
        // it is kept precisely so this can be shown.
        string raw =
            $"Ray Delgado{ValueMark}Marta Quinn{AttributeMark}" +
            $"303-555-0100{SubvalueMark}720-555-0101{ValueMark}303-555-0102";

        IReadOnlyList<RecordMark> marks = RecordMarks.PresentIn(raw);

        Assert.Equal(3, marks.Count);
    }

    [Fact]
    public void A_record_with_no_marks_at_all_describes_none()
    {
        // A single-field record is a legitimate record. Describing a mark it does
        // not contain would put a badge on screen with nothing behind it.
        Assert.Empty(RecordMarks.PresentIn("DEN"));
    }

    [Fact]
    public void An_empty_record_describes_none()
    {
        Assert.Empty(RecordMarks.PresentIn(string.Empty));
    }

    [Fact]
    public void Marks_are_described_outermost_first()
    {
        // The order is the nesting. A legend that listed subvalues before fields
        // would read as though values contained fields.
        string raw = $"a{SubvalueMark}b{ValueMark}c{AttributeMark}d";

        IReadOnlyList<RecordMark> marks = RecordMarks.PresentIn(raw);

        Assert.Equal([254, 253, 252], marks.Select(mark => mark.Code));
    }

    [Theory]
    [InlineData(AttributeMark, 254)]
    [InlineData(ValueMark, 253)]
    [InlineData(SubvalueMark, 252)]
    public void Each_mark_reports_the_code_point_it_is_known_by(char mark, int code)
    {
        // 254, 253 and 252 are how these are referred to in every MultiValue
        // reference and in every conversation about one. Showing the character
        // without its number would leave a reader unable to look it up.
        RecordMark described = Assert.Single(RecordMarks.PresentIn($"a{mark}b"));

        Assert.Equal(code, described.Code);
        Assert.Equal(mark, described.Character);
    }

    [Fact]
    public void Every_description_says_what_the_mark_separates()
    {
        // Written for someone who has never seen a MultiValue record, because
        // that is who this drawer is explaining the format to.
        string raw = $"a{SubvalueMark}b{ValueMark}c{AttributeMark}d";

        Assert.All(RecordMarks.PresentIn(raw), mark =>
        {
            Assert.False(string.IsNullOrWhiteSpace(mark.Name));
            Assert.False(string.IsNullOrWhiteSpace(mark.Separates));
        });
    }

    [Fact]
    public void A_mark_appearing_many_times_is_described_once()
    {
        // The legend explains the format. Repeating an entry per occurrence would
        // make a long record produce a longer legend than record.
        string raw = $"a{AttributeMark}b{AttributeMark}c{AttributeMark}d";

        Assert.Single(RecordMarks.PresentIn(raw));
    }
}
