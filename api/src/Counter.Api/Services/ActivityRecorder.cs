namespace Counter.Api.Services;

using System.Collections.Concurrent;
using Counter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// What was asked of the system, by whom, and how it went.
/// </summary>
/// <remarks>
/// An audit trail that cannot name a person is a debugging aid, not an audit
/// trail. Every request writes exactly one entry, including the ones that fail —
/// a failure nobody recorded is indistinguishable from a request nobody made.
///
/// Entries go to two places. The durable store is the audit trail proper, and is
/// what a reviewer reads. The in-memory ring is what the activity panel reads, so
/// showing someone their last ten actions never waits on a database — and so the
/// panel still works when no database is configured, which is how the
/// demonstration runs on a laptop.
///
/// Nothing written here carries a credential. Values that came from what a person
/// typed pass through <see cref="SecretRedactor"/> first, because someone will
/// eventually paste a password into a search box.
/// </remarks>
/// <param name="redactor">Removes configured secrets before anything is stored.</param>
/// <param name="logger">For reporting a durable write that could not be made.</param>
/// <param name="contexts">The durable store, absent when none is configured.</param>
public sealed class ActivityRecorder(
    SecretRedactor redactor,
    ILogger<ActivityRecorder> logger,
    IDbContextFactory<CounterContext>? contexts = null)
{
    /// <summary>How many entries to keep in memory. Beyond this the oldest go.</summary>
    private const int Capacity = 500;

    private readonly ConcurrentQueue<ActivityEntry> _entries = new();
    private readonly SecretRedactor _redactor = redactor;
    private readonly ILogger<ActivityRecorder> _logger = logger;
    private readonly IDbContextFactory<CounterContext>? _contexts = contexts;

    /// <summary>
    /// Set false the moment the durable store is known not to be working.
    /// </summary>
    /// <remarks>
    /// Volatile because it is written by whichever request first fails and read
    /// by the health endpoint on another thread, and a stale read here would be
    /// the endpoint reporting the very thing this field exists to correct.
    /// </remarks>
    private volatile bool _durableStoreIsWorking = contexts is not null;

    /// <summary>Whether entries are reaching a durable store as well as memory.</summary>
    /// <remarks>
    /// Whether anything is being <em>stored</em>, not whether a database has been
    /// <em>configured</em>. Those are different questions, and answering the
    /// second while appearing to answer the first is how the deployed
    /// application came to report a healthy audit trail for hours while every
    /// write failed on a table that had never been created.
    ///
    /// So the claim is withdrawn by evidence rather than only asserted at
    /// startup: a migration that could not run says so, and so does the first
    /// write that fails. A claim that can only be set once is a claim that can
    /// outlive its truth, and this one is read by someone deciding whether the
    /// trail can be relied on.
    /// </remarks>
    public bool IsDurable => _contexts is not null && _durableStoreIsWorking;

    /// <summary>
    /// Record that the durable store could not be opened.
    /// </summary>
    /// <remarks>
    /// Called by startup when the schema could not be brought up to date. Without
    /// this the application would go on offering an audit trail it had already
    /// discovered it did not have.
    /// </remarks>
    public void ReportDurableStoreUnavailable() => _durableStoreIsWorking = false;

    /// <summary>
    /// Record one request.
    /// </summary>
    /// <param name="entry">What happened.</param>
    /// <remarks>
    /// Takes no cancellation token, and that is the point of its signature.
    ///
    /// The durable write happens after the action has run. Handed the request's
    /// own token it would be cancelled by the very thing it exists to record --
    /// a caller who goes away -- so anyone wanting their queries unlogged would
    /// only have to stop waiting for the answers.
    ///
    /// The filter used to pass <c>CancellationToken.None</c> with a comment
    /// explaining why. A comment stops no one from changing the argument, and a
    /// test that raced a real client's cancellation against a real request was
    /// flaky enough to fail a deployment. Removing the parameter settles it in
    /// the type system: there is no longer an argument to get wrong.
    ///
    /// A failure to write the audit row is logged and swallowed. The alternative
    /// turns a successful answer into an error the user sees, which would mean an
    /// audit problem denying service — and the in-memory copy plus the MCP
    /// server's own log still hold the event.
    /// </remarks>
    public async Task RecordAsync(ActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        ActivityEntry safe = entry with
        {
            TargetKey = _redactor.Redact(entry.TargetKey),
            DatabaseLogin = _redactor.Redact(entry.DatabaseLogin),
        };

        Remember(safe);

        if (_contexts is null)
        {
            return;
        }

        try
        {
            await using CounterContext context =
                await _contexts.CreateDbContextAsync();

            context.Activity.Add(new ActivityRow
            {
                OccurredAt = safe.OccurredAt,
                UserSubject = safe.UserSubject,
                DisplayName = safe.DisplayName,
                Action = safe.Action,
                TargetKey = safe.TargetKey,
                DatabaseLogin = safe.DatabaseLogin,
                DatabaseLoginIsShared = safe.DatabaseLoginIsShared,
                DurationMs = safe.DurationMs,
                Outcome = safe.Outcome,
            });

            await context.SaveChangesAsync();
        }
#pragma warning disable CA1031 // An audit write must never deny service.
        catch (Exception error)
#pragma warning restore CA1031
        {
            // Withdrawn here rather than only logged. The log said this for
            // hours while the health endpoint went on reporting a durable audit
            // trail, and nobody reads a log to find out whether a claim on a
            // status page is true.
            _durableStoreIsWorking = false;

            _logger.LogError(
                error,
                "The activity record for {Action} by {Subject} could not be stored",
                safe.Action,
                safe.UserSubject);
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

    /// <summary>Add to the in-memory ring, dropping the oldest past capacity.</summary>
    private void Remember(ActivityEntry entry)
    {
        _entries.Enqueue(entry);

        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
            // Oldest first. A bounded record is better than an unbounded one that
            // eventually consumes the process.
        }
    }
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
/// <param name="Outcome">
/// Success, NotFound, Unreachable, Refused, MalformedRecord or Failed. The set is
/// produced by <c>ActivityRecordingFilter.DescribeOutcome</c> and nowhere else.
/// </param>
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
