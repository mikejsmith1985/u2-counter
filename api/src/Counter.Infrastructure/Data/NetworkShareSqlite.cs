namespace Counter.Infrastructure.Data;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Settings SQLite needs when its file lives on a mounted network share.
/// </summary>
/// <remarks>
/// The audit database sits on an Azure Files share so that it survives a
/// container that scales to zero. That share is mounted over SMB, and SMB does
/// not honour the byte-range locking SQLite uses to coordinate writers.
///
/// The symptom was not a warning. Bringing the schema up to date issued
/// `CREATE TABLE IF NOT EXISTS "__EFMigrationsLock"`, waited the full command
/// timeout, and failed with "database is locked" — so the audit tables were never
/// created, every write afterwards failed on a table that did not exist, and the
/// application blocked for thirty seconds on every cold start before serving its
/// first request. One unsupported lock, three separate faults.
///
/// Two pragmas answer it, and both are correct here rather than merely
/// convenient:
///
///   - `locking_mode=EXCLUSIVE` takes the file lock once and keeps it, instead of
///     acquiring and releasing around every transaction. That is what SMB
///     mishandles. It is safe because this application runs at one replica and is
///     the only writer; a second writer would be refused rather than corrupt
///     anything, which is the failure worth having.
///
///   - `journal_mode=DELETE` is SQLite's own default and is set explicitly
///     because the alternative must never be chosen here: write-ahead logging
///     uses shared memory that network filesystems do not provide, and a future
///     edit turning WAL on would break this in a way that looks unrelated.
///
/// The remaining risk is stated rather than hidden: an exclusive lock is released
/// only when the connection closes, so a container killed mid-write leaves the
/// file locked until the mount drops the handle. For an audit trail on a
/// single-replica demonstration that is the right trade against not being able to
/// write at all.
/// </remarks>
public sealed class NetworkShareSqlite : DbConnectionInterceptor
{
    /// <summary>
    /// How long any one statement may take.
    /// </summary>
    /// <remarks>
    /// Well under the platform's patience for a container that has not begun
    /// listening. The default of thirty seconds meant a failing migration held
    /// the port closed long enough for the platform to record thirty consecutive
    /// failed start-up probes, turning an audit problem into a slow cold start
    /// for everybody.
    /// </remarks>
    public static readonly TimeSpan CommandBudget = TimeSpan.FromSeconds(8);

    /// <inheritdoc />
    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Apply(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    /// <summary>
    /// Set the pragmas on a freshly opened connection.
    /// </summary>
    /// <param name="connection">The connection to configure.</param>
    /// <remarks>
    /// Failures are swallowed on purpose. These settings make the database work
    /// on a network share; a provider that does not recognise them — an in-memory
    /// SQLite database in a test, say — is not a reason to refuse to start, and
    /// the audit trail already reports its own availability by whether writes
    /// actually succeed.
    /// </remarks>
    private static void Apply(DbConnection connection)
    {
        try
        {
            using DbCommand command = connection.CreateCommand();

            command.CommandText =
                "PRAGMA journal_mode=DELETE; PRAGMA locking_mode=EXCLUSIVE;";

            command.ExecuteNonQuery();
        }
#pragma warning disable CA1031 // A pragma that will not apply must not stop startup.
        catch (DbException)
#pragma warning restore CA1031
        {
            // Left to the caller's own error handling. Every path that writes
            // here already reports whether it succeeded.
        }
    }
}
