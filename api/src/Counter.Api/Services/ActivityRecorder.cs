namespace Counter.Api.Services;

using System.Collections.Concurrent;

/// <summary>
/// What was asked of the system, by whom, and how it went.
/// </summary>
/// <remarks>
/// An audit trail that cannot name a person is a debugging aid, not an audit
/// trail. Every request writes exactly one entry, including the ones that fail —
/// a failure nobody recorded is indistinguishable from a request nobody made.
///
/// Entries are held in memory for this demonstration. The MCP server keeps its
/// own durable record; this mirror exists so the activity panel can show a user
/// their own recent work without exposing the server's log files to a browser.
/// </remarks>
public sealed class ActivityRecorder
{
    /// <summary>How many entries to keep. Beyond this the oldest are dropped.</summary>
    private const int Capacity = 500;

    private readonly ConcurrentQueue<ActivityEntry> _entries = new();

    /// <summary>Record one request.</summary>
    /// <param name="entry">What happened.</param>
    public void Record(ActivityEntry entry)
    {
        _entries.Enqueue(entry);

        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
            // Oldest first. A bounded record is better than an unbounded one that
            // eventually consumes the process.
        }
    }

    /// <summary>
    /// Read recent activity for one person.
    /// </summary>
    /// <param name="userSubject">Whose activity to return.</param>
    /// <param name="limit">Most entries to return.</param>
    /// <remarks>
    /// Scoped to the caller. Someone reviewing their own work has a reason to;
    /// browsing everyone else's is a different feature with different consent.
    /// </remarks>
    public IReadOnlyList<ActivityEntry> Recent(string userSubject, int limit) =>
        _entries
            .Where(entry => entry.UserSubject == userSubject)
            .Reverse()
            .Take(limit)
            .ToList();
}

/// <summary>
/// One recorded request.
/// </summary>
/// <param name="OccurredAt">When.</param>
/// <param name="UserSubject">Who, as the identity provider names them.</param>
/// <param name="DisplayName">Who, as a person would recognise them.</param>
/// <param name="Action">What was asked for.</param>
/// <param name="TargetKey">What it was asked about.</param>
/// <param name="DatabaseLogin">Which account served it.</param>
/// <param name="DatabaseLoginIsShared">
/// Whether that account is used by more than one person. Recorded because a
/// reviewer needs to know when the database could not tell callers apart.
/// </param>
/// <param name="DurationMs">How long it took.</param>
/// <param name="Outcome">Success, NotFound, Unreachable or Refused.</param>
public sealed record ActivityEntry(
    DateTimeOffset OccurredAt,
    string UserSubject,
    string DisplayName,
    string Action,
    string TargetKey,
    string DatabaseLogin,
    bool DatabaseLoginIsShared,
    int DurationMs,
    string Outcome);
