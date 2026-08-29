namespace Counter.Infrastructure.Data;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

/// <summary>
/// The few things a file-backed database needs that a server-backed one does not.
/// </summary>
/// <remarks>
/// SQLite was chosen because this application scales to zero. A database server
/// is the one component that cannot scale down with it, and it would sit there
/// costing money to hold a few thousand audit rows nobody is reading. A file on
/// a mounted share costs nothing while idle.
///
/// What that buys in cost it charges back in care, and both charges are here:
/// the directory has to exist before SQLite will open anything in it, and the
/// share it sits on is SMB, which is where SQLite's locking assumptions stop
/// being safe with more than one writer.
/// </remarks>
public static class CounterDatabase
{
    /// <summary>
    /// Create the directory the database file will live in, if it is missing.
    /// </summary>
    /// <param name="connectionString">The connection string naming the file.</param>
    /// <param name="logger">For saying what was created, and what could not be.</param>
    /// <remarks>
    /// On a freshly mounted share the directory often does not exist yet, and
    /// SQLite reports that as "unable to open database file" — which reads like
    /// a permissions problem and sends whoever hit it looking in the wrong place.
    ///
    /// A failure here is logged and swallowed. The application runs without a
    /// durable store, and refusing to start because the audit trail could not be
    /// created would take the whole thing down for the one part of it that is
    /// explicitly optional.
    /// </remarks>
    public static void EnsureDirectoryExists(string? connectionString, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            SqliteConnectionStringBuilder builder = new(connectionString);
            string? directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));

            if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            logger.LogInformation("Created {Directory} for the audit trail", directory);
        }
#pragma warning disable CA1031 // A missing audit trail must not stop the application.
        catch (Exception error)
#pragma warning restore CA1031
        {
            logger.LogError(
                error,
                "The directory for the audit trail could not be created. The application will " +
                "run without a durable record of who asked what");
        }
    }
}
