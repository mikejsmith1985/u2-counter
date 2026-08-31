namespace Counter.UnitTests.Mcp;

using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// What the write path refuses before it touches the database, and how it
/// recognises a record whose shape has moved.
/// </summary>
/// <remarks>
/// MultiValue has no constraints to break, so a record that is now wrong is
/// still a perfectly valid record. Nothing errors, every later read agrees with
/// it, and the first anybody hears of it is a customer being promised stock
/// that is at another branch.
///
/// Two defences, and both were untested. The guard refuses a value carrying a
/// separator before anything is written; the field-length comparison catches a
/// record whose shape moved anyway. The guard exists because the comparison
/// alone was reporting "written, but a field changed length" — honest, and far
/// too late to be any use.
/// </remarks>
public sealed class WriteRefusalTests
{
    /// <summary>Separates fields.</summary>
    private const char AttributeMark = (char)254;

    /// <summary>Separates values within a field.</summary>
    private const char ValueMark = (char)253;

    /// <summary>Separates subvalues within a value.</summary>
    private const char SubvalueMark = (char)252;

    /// <summary>A reader that must never be reached, because the guard runs first.</summary>
    private sealed class UnreachableReader : IErpReader
    {
        /// <inheritdoc />
        public Task<string> ReadRecordAsync(string fileName, string recordId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The guard should have refused before anything was read.");

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> SelectKeysAsync(string query, int maxKeys, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not reached.");

        /// <inheritdoc />
        public Task<IReadOnlyList<Counter.Domain.Catalogue.DictionaryField>> ListDictionaryAsync(string fileName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not reached.");

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not reached.");

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, string>> ReadRecordsAsync(string fileName, IReadOnlyList<string> recordIds, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not reached.");
    }

    /// <summary>A writer wired to a reader that throws if it is used.</summary>
    /// <returns>The writer under test.</returns>
    private static ErpWriter NewWriter() =>
        new(
            new UnreachableReader(),
            Options.Create(new ErpConnectionOptions()),
            NullLogger<ErpWriter>.Instance);

    [Theory]
    [InlineData(AttributeMark)]
    [InlineData(ValueMark)]
    [InlineData(SubvalueMark)]
    public async Task A_value_carrying_a_separator_is_refused_before_anything_is_read(char mark)
    {
        // The attribute mark is the one that was missed. A value mark adds a
        // value to a field; an attribute mark splits the field in two, so a
        // four-field record becomes five and every field after the first
        // describes something it is not.
        ErpWriter writer = NewWriter();

        ErpWriteRefusedException refused =
            await Assert.ThrowsAsync<ErpWriteRefusedException>(() =>
                writer.UpdateValueAsync(
                    "BRANCH", "AUR", 1, 0, $"one{mark}two", CancellationToken.None));

        Assert.Contains("shape", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_ordinary_value_is_not_refused_by_the_guard()
    {
        // The guard must let normal text past, or the feature does nothing. It
        // gets as far as the read, which is where this fake stops it — proof it
        // passed the guard rather than proof the write succeeded.
        ErpWriter writer = NewWriter();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.UpdateValueAsync(
                "BRANCH", "AUR", 1, 0, "AURORA", CancellationToken.None));
    }

    [Fact]
    public void Field_lengths_count_the_values_in_each_field()
    {
        // The shape that has to survive a write.
        string record = string.Join(AttributeMark,
            "LKW" + ValueMark + "GRE" + ValueMark + "AUR",
            "16" + ValueMark + "16" + ValueMark + "14",
            "DEN");

        Assert.Equal([3, 3, 1], ErpWriter.FieldLengths(record));
    }

    [Fact]
    public void A_field_that_gained_a_value_shows_as_a_different_shape()
    {
        // The failure this whole path exists for: the branch list and the
        // quantity list must stay the same length, or position three's quantity
        // is read against position four's branch.
        string before = "LKW" + ValueMark + "GRE" + AttributeMark + "16" + ValueMark + "16";
        string after = "LKW" + ValueMark + "GRE" + ValueMark + "AUR" + AttributeMark + "16" + ValueMark + "16";

        Assert.NotEqual(ErpWriter.FieldLengths(before), ErpWriter.FieldLengths(after));
    }

    [Fact]
    public void Changing_a_value_in_place_leaves_the_shape_alone()
    {
        // The write that is allowed. Same counts, different contents.
        string before = "LKW" + ValueMark + "GRE" + AttributeMark + "16" + ValueMark + "16";
        string after = "AURORA" + ValueMark + "GRE" + AttributeMark + "16" + ValueMark + "16";

        Assert.Equal(ErpWriter.FieldLengths(before), ErpWriter.FieldLengths(after));
    }

    [Fact]
    public void An_empty_record_has_no_fields_rather_than_one_empty_one()
    {
        // Splitting an empty string yields one empty piece, which would report
        // a shape of [1] and make a deleted record look like a one-field one.
        Assert.Empty(ErpWriter.FieldLengths(string.Empty));
    }

    [Fact]
    public void A_trailing_empty_value_still_counts()
    {
        // A field ending in a value mark holds an empty last value. Dropping it
        // would make a shortened field look unchanged.
        string record = "LKW" + ValueMark + "GRE" + ValueMark;

        Assert.Equal([3], ErpWriter.FieldLengths(record));
    }

    /// <summary>Build a change from two records, as the writer does.</summary>
    /// <param name="before">The stored form before.</param>
    /// <param name="after">The stored form after.</param>
    /// <returns>The change and its verdict.</returns>
    private static RecordChange Changed(string before, string after) =>
        new("AUR", before, after, ErpWriter.FieldLengths(before), ErpWriter.FieldLengths(after));

    [Fact]
    public void A_value_replaced_in_place_preserves_the_alignment()
    {
        // The verdict the whole write path exists to produce, and the one shown
        // to a person as proof their change did what they meant.
        RecordChange change = Changed(
            "LKW" + ValueMark + "GRE" + AttributeMark + "16" + ValueMark + "16",
            "AURORA" + ValueMark + "GRE" + AttributeMark + "16" + ValueMark + "16");

        Assert.True(change.IsAlignmentPreserved);
    }

    [Fact]
    public void A_field_that_gained_a_value_fails_the_alignment_check()
    {
        // Both records are well formed. The database will not object, no later
        // read will disagree, and branch three's quantity now describes branch
        // four. The lengths are the only place it shows.
        RecordChange change = Changed(
            "LKW" + ValueMark + "GRE" + AttributeMark + "16" + ValueMark + "16",
            "LKW" + ValueMark + "GRE" + ValueMark + "AUR" + AttributeMark + "16" + ValueMark + "16");

        Assert.False(change.IsAlignmentPreserved);
    }

    [Fact]
    public void A_record_that_gained_a_field_fails_the_alignment_check()
    {
        // What an attribute mark in a value does. Caught here as well as
        // refused up front, because being caught afterwards was the old
        // behaviour and it was not enough.
        RecordChange change = Changed(
            "LKW" + AttributeMark + "16",
            "LKW" + AttributeMark + "16" + AttributeMark + "extra");

        Assert.False(change.IsAlignmentPreserved);
    }

    [Fact]
    public void A_field_that_lost_a_value_fails_too()
    {
        // The other direction, and the more likely one: rebuilding a record
        // drops a field that happened to be shorter than its siblings, and
        // everything after it shifts up by one.
        RecordChange change = Changed(
            "LKW" + ValueMark + "GRE" + ValueMark + "AUR" + AttributeMark + "16",
            "LKW" + ValueMark + "GRE" + AttributeMark + "16");

        Assert.False(change.IsAlignmentPreserved);
    }

    [Fact]
    public void A_record_unchanged_is_aligned_with_itself()
    {
        // A write that changed a value to what it already held is not a
        // failure, and must not read as one.
        string record = "LKW" + ValueMark + "GRE" + AttributeMark + "16" + ValueMark + "16";

        Assert.True(Changed(record, record).IsAlignmentPreserved);
    }
}
