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
    public async Task The_lock_is_taken_once_and_held()
    {
        // `locking_mode=EXCLUSIVE` is the setting that makes this work on a
        // share: SQLite stops acquiring and releasing the file lock around every
        // transaction, which is the pattern SMB does not honour.
        await using CounterContext context = NewContext();
        await context.Database.OpenConnectionAsync();

        Assert.Equal("exclusive", await ReadPragmaAsync(context, "locking_mode"));
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
