namespace Counter.IntegrationTests;

using Counter.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The settings that let the audit database live on a mounted share.
/// </summary>
/// <remarks>
/// These assert that the pragmas are actually in force on a connection the
/// application hands out, not that they were written down. The defect they
/// answer was configuration that looked right and did nothing: the database
/// file existed, the connection opened, and the schema was never created because
/// the lock behind `CREATE TABLE` could not be taken over SMB.
///
/// What cannot be tested here is SMB itself — this runs on a local disk, where
/// the old settings worked fine, which is exactly why the problem reached the
/// deployment. So these tests pin the configuration, and the deployment is
/// checked by reading `isAuditDurable` from the running application.
/// </remarks>
public sealed class NetworkShareSqliteTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"counter-{Guid.NewGuid():N}.db");

    /// <summary>A context configured the way the application configures its own.</summary>
    private CounterContext NewContext() =>
        new(new DbContextOptionsBuilder<CounterContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .AddInterceptors(new NetworkShareSqlite())
            .Options);

    [Fact]
    public async Task Bringing_the_schema_up_to_date_does_not_stall()
    {
        // The regression guard for a fix that was worse than the defect.
        //
        // `locking_mode=EXCLUSIVE` was the first attempt at the share problem. It
        // passed every other test in this file and made every migration take
        // thirty seconds, because applying one needs a second connection and
        // that connection waited out the busy timeout. Thirty seconds is also
        // exactly what the original defect cost, so the fix reproduced the
        // symptom it was chosen to remove.
        //
        // A second and a half is generous for creating two tables and leaves no
        // room for a stall.
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

        await using CounterContext context = NewContext();
        await context.Database.MigrateAsync();

        timer.Stop();

        Assert.True(
            timer.Elapsed < TimeSpan.FromSeconds(1.5),
            $"Bringing the schema up to date took {timer.Elapsed.TotalSeconds:0.0}s. " +
            "Something is waiting on a lock, which on the deployed share is the " +
            "difference between an audit trail and an empty file.");
    }

    [Fact]
    public async Task Write_ahead_logging_is_not_used()
    {
        // WAL relies on shared memory that a network filesystem does not
        // provide. It is off by default, and pinned here because turning it on
        // would break the deployment in a way that looks nothing like its cause.
        await using CounterContext context = NewContext();
        await context.Database.OpenConnectionAsync();

        Assert.Equal("delete", await ReadPragmaAsync(context, "journal_mode"));
    }

    [Fact]
    public async Task The_schema_can_still_be_created_and_written_to()
    {
        // The settings must not cost the database its job. This is the sequence
        // that failed in the deployment: create the schema, then write a row.
        await using CounterContext context = NewContext();
        await context.Database.MigrateAsync();

        context.Activity.Add(new ActivityRow
        {
            OccurredAt = DateTimeOffset.UtcNow,
            UserSubject = "demo|dana",
            DisplayName = "Dana",
            Action = "Searched",
            TargetKey = "breaker",
            DatabaseLogin = "u2demo@DEMO",
            DatabaseLoginIsShared = true,
            DurationMs = 4,
            Outcome = "Success",
        });

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_second_context_can_still_write_after_the_first()
    {
        // The risk the exclusive lock introduces, and the one that would make
        // this fix worse than the defect.
        //
        // The application writes each audit row from its own context, because
        // the filter that records a request outlives the request's scope. If an
        // exclusive lock taken by one connection outlived that connection, the
        // first row would be written and every row after it would fail --
        // turning an audit trail that stored nothing into one that stored
        // exactly one thing, which is harder to notice and no more useful.
        await using (CounterContext first = NewContext())
        {
            await first.Database.MigrateAsync();
            first.Activity.Add(AnEntry("first"));
            await first.SaveChangesAsync();
        }

        await using CounterContext second = NewContext();
        second.Activity.Add(AnEntry("second"));

        Assert.Equal(1, await second.SaveChangesAsync());

        // And both are there, which is the actual requirement.
        await using CounterContext reader = NewContext();
        Assert.Equal(2, reader.Activity.Count());
    }

    [Fact]
    public async Task Many_writes_in_sequence_all_land()
    {
        // A counter answering questions all morning, compressed. One row per
        // request, each from its own context, as the filter does it.
        await using (CounterContext setUp = NewContext())
        {
            await setUp.Database.MigrateAsync();
        }

        for (int request = 0; request < 20; request++)
        {
            await using CounterContext context = NewContext();
            context.Activity.Add(AnEntry($"request-{request}"));
            await context.SaveChangesAsync();
        }

        await using CounterContext reader = NewContext();
        Assert.Equal(20, reader.Activity.Count());
    }

    [Fact]
    public async Task Writes_that_overlap_all_land()
    {
        // The case that decides whether these settings are usable at all.
        //
        // Twelve simultaneous requests against the deployed application is not a
        // stress test, it is one person on the phone while another uses the
        // screen. Each one writes its own audit row from its own context, so the
        // contexts are alive at the same time and hold different connections.
        //
        // An exclusive file lock held for a connection's lifetime would let the
        // first of them write and refuse the rest -- and because a failed audit
        // write is swallowed by design, the loss would be silent. That is a
        // worse failure than the one being fixed, so it is asserted rather than
        // reasoned about.
        await using (CounterContext setUp = NewContext())
        {
            await setUp.Database.MigrateAsync();
        }

        const int Callers = 12;

        await Task.WhenAll(Enumerable.Range(0, Callers).Select(async caller =>
        {
            await using CounterContext context = NewContext();
            context.Activity.Add(AnEntry($"caller-{caller}"));
            await context.SaveChangesAsync();
        }));

        await using CounterContext reader = NewContext();
        Assert.Equal(Callers, reader.Activity.Count());
    }

    /// <summary>One entry, named so a test can tell them apart.</summary>
    private static ActivityRow AnEntry(string target) => new()
    {
        OccurredAt = DateTimeOffset.UtcNow,
        UserSubject = "demo|dana",
        DisplayName = "Dana",
        Action = "Searched",
        TargetKey = target,
        DatabaseLogin = "u2demo@DEMO",
        DatabaseLoginIsShared = true,
        DurationMs = 4,
        Outcome = "Success",
    };

    /// <summary>Read one pragma back from the open connection.</summary>
    private static async Task<string> ReadPragmaAsync(CounterContext context, string pragma)
    {
        await using SqliteCommand command =
            (SqliteCommand)context.Database.GetDbConnection().CreateCommand();

        command.CommandText = $"PRAGMA {pragma};";

        object? value = await command.ExecuteScalarAsync();
        return value?.ToString()?.ToLowerInvariant() ?? string.Empty;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
