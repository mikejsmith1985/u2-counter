namespace Counter.Infrastructure.Mcp;

/// <summary>
/// Changing a record, when a deployment has asked for that to be possible.
/// </summary>
/// <remarks>
/// Deliberately not part of <see cref="IErpReader"/>, and that separation is the
/// design rather than tidiness.
///
/// The read-only guarantee elsewhere in this application is stated as "no
/// mutating verb exists in the path to the database", and a test asserts it by
/// walking the reader. Adding a write to that interface would have made the
/// sentence false and the test a formality. Keeping it here leaves both intact:
/// the reader genuinely still has no write, and anything holding only a reader
/// cannot acquire one.
///
/// Nothing is registered against this interface unless a deployment configures a
/// writable ERP driver. The demonstration does not, so on the deployed
/// application the implementation is absent and the endpoints that need it report
/// that plainly rather than failing.
///
/// Every method reads the record back after changing it. MultiValue has no
/// constraints, no foreign keys and no transaction by default, so the database
/// will not catch a bad write on your behalf -- the only honest confirmation that
/// a change did what was intended is to look.
/// </remarks>
public interface IErpWriter
{
    /// <summary>
    /// Change one value of one field, leaving every other position where it was.
    /// </summary>
    /// <param name="fileName">The MultiValue file.</param>
    /// <param name="recordId">The record's key.</param>
    /// <param name="position">Which field, counting from one.</param>
    /// <param name="index">Which value within that field, counting from zero.</param>
    /// <param name="value">What to put there.</param>
    /// <param name="cancellationToken">Abandons the work when the caller gives up.</param>
    /// <returns>What the record looked like before and after, read back from the file.</returns>
    /// <remarks>
    /// One value, in place. Not a rewrite of the record: a parallel field may
    /// legitimately be shorter than its siblings, so a write that pads to reach
    /// an index invents positions -- and in an inventory record a position is a
    /// claim about a branch.
    /// </remarks>
    Task<RecordChange> UpdateValueAsync(
        string fileName,
        string recordId,
        int position,
        int index,
        string value,
        CancellationToken cancellationToken);
}

/// <summary>
/// A record before and after a change, as the database returned it both times.
/// </summary>
/// <param name="RecordId">The record that changed.</param>
/// <param name="Before">Its stored form before, read immediately prior.</param>
/// <param name="After">Its stored form after, read back from the file.</param>
/// <param name="FieldLengthsBefore">How many values each field held before.</param>
/// <param name="FieldLengthsAfter">
/// How many values each field holds now. Reported beside the before so a reader
/// can see for themselves that no parallel field changed length -- which is the
/// specific damage a careless write does, and the one that reports nothing.
/// </param>
public sealed record RecordChange(
    string RecordId,
    string Before,
    string After,
    IReadOnlyList<int> FieldLengthsBefore,
    IReadOnlyList<int> FieldLengthsAfter)
{
    /// <summary>Whether every field still holds as many values as it did.</summary>
    /// <remarks>
    /// The check a MultiValue database will not do for you. Two records can both
    /// be well formed while one of them has quietly moved a quantity onto a
    /// different branch, and the lengths are where that shows.
    /// </remarks>
    public bool IsAlignmentPreserved =>
        FieldLengthsBefore.Count == FieldLengthsAfter.Count &&
        FieldLengthsBefore.SequenceEqual(FieldLengthsAfter);
}
