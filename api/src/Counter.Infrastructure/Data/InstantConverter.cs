namespace Counter.Infrastructure.Data;

using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>
/// Stores an instant as text that sorts the way the instants do.
/// </summary>
/// <remarks>
/// SQLite has no date type and no offset type, so the provider stores a
/// <see cref="DateTimeOffset"/> as text in a format it will not compare — every
/// `WHERE occurred &gt; x` and every `ORDER BY occurred` fails to translate, at
/// runtime, with a message about LINQ rather than about dates.
///
/// The fix is to choose the text ourselves. ISO-8601 in UTC to seven decimal
/// places sorts lexicographically in exactly the order the instants happened, so
/// the database can compare and order them as ordinary strings.
///
/// Text rather than a tick count, deliberately. This is an audit trail, and the
/// point of one is that somebody opens it and reads it. A column of nineteen-digit
/// integers is unreadable to the person the table exists to serve.
///
/// Everything is stored in UTC. The offset a row was written with is not kept,
/// because it is the offset of the server rather than of anyone involved — and a
/// trail whose ordering depended on where its rows were written would be worse
/// than one that simply records instants.
/// </remarks>
public sealed class InstantConverter : ValueConverter<DateTimeOffset, string>
{
    /// <summary>
    /// Round-trip UTC, to the tick.
    /// </summary>
    /// <remarks>
    /// Fixed width matters as much as the ordering: "9" sorts after "10" as text,
    /// so a format that dropped trailing zeroes would order rows wrongly for as
    /// long as nobody looked closely.
    /// </remarks>
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    /// <summary>Create the converter.</summary>
    public InstantConverter()
        : base(
            instant => instant.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture),
            stored => Parse(stored))
    {
    }

    /// <summary>Read a stored instant back.</summary>
    /// <param name="stored">The text as written by this converter.</param>
    private static DateTimeOffset Parse(string stored) =>
        new(DateTime.ParseExact(
            stored,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            TimeSpan.Zero);
}
