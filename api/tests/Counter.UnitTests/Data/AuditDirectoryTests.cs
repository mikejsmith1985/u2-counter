namespace Counter.UnitTests.Data;

using Counter.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Making room for the audit trail, without ever stopping the application.
/// </summary>
/// <remarks>
/// This runs at start-up, before anything is serving. It exists because SQLite
/// will not create the directory its file lives in, and on a freshly mounted
/// share that directory often does not exist yet -- SQLite reports the miss as
/// "unable to open database file", which reads like a permissions problem and
/// sends whoever hit it looking in the wrong place.
///
/// The rule that matters is that it must never throw. The audit trail is the
/// one part of this deployment that is explicitly optional, and refusing to
/// start because it could not be created would take the whole application down
/// for the piece that was allowed to be missing. This project has already lost
/// a deployment to a start-up failure once, which is why every path here is a
/// path that returns.
/// </remarks>
public sealed class AuditDirectoryTests
{
    /// <summary>A directory path under the test's own temporary space.</summary>
    /// <param name="child">A name unique to the test.</param>
    /// <returns>A path that does not exist yet.</returns>
    private static string SomewhereNew(string child) =>
        Path.Combine(Path.GetTempPath(), "counter-tests", Guid.NewGuid().ToString("N"), child);

    [Fact]
    public void The_directory_is_created_when_it_is_missing()
    {
        // The ordinary case on a fresh share, and the reason this exists.
        string directory = SomewhereNew("audit");
        string file = Path.Combine(directory, "counter.db");

        try
        {
            CounterDatabase.EnsureDirectoryExists(
                $"Data Source={file}", NullLogger.Instance);

            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void A_directory_that_already_exists_is_left_alone()
    {
        // Every restart takes this path. Recreating or clearing it would throw
        // away the audit trail on a scale-from-zero, which is routine here.
        string directory = SomewhereNew("audit");
        Directory.CreateDirectory(directory);
        string existing = Path.Combine(directory, "already-here.txt");
        File.WriteAllText(existing, "kept");

        try
        {
            CounterDatabase.EnsureDirectoryExists(
                $"Data Source={Path.Combine(directory, "counter.db")}", NullLogger.Instance);

            Assert.True(File.Exists(existing));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_connection_string_is_a_deployment_without_a_durable_trail(string? connectionString)
    {
        // A supported configuration rather than a mistake: the application runs,
        // and says on screen that the trail is not durable.
        CounterDatabase.EnsureDirectoryExists(connectionString, NullLogger.Instance);
    }

    [Fact]
    public void A_connection_string_it_cannot_parse_does_not_stop_the_application()
    {
        // The rule. Anything unexpected here is logged and swallowed, because
        // the alternative is a deployment that will not start over its optional
        // half.
        CounterDatabase.EnsureDirectoryExists("this is not a connection string", NullLogger.Instance);
    }

    [Fact]
    public void A_path_the_operating_system_refuses_does_not_stop_the_application()
    {
        // Characters no filesystem will accept, standing in for the share that
        // is mounted read-only or not mounted at all. The point is not which
        // error arrives; it is that none of them escapes.
        string impossible = "Data Source=" + new string('\0', 1) + "??|<>*.db";

        CounterDatabase.EnsureDirectoryExists(impossible, NullLogger.Instance);
    }

    [Fact]
    public void A_file_name_with_no_directory_at_all_is_accepted()
    {
        // A bare file name resolves against the working directory, which
        // already exists. Nothing to create, and nothing to complain about.
        CounterDatabase.EnsureDirectoryExists("Data Source=counter.db", NullLogger.Instance);
    }

    [Fact]
    public void A_logger_is_required_because_a_silent_failure_here_is_the_worst_outcome()
    {
        // Everything else about this is forgiving. The one thing it will not do
        // is swallow a failure with nowhere to report it: a deployment running
        // without an audit trail and no line saying why is the state nobody
        // would ever diagnose.
        Assert.Throws<ArgumentNullException>(() =>
            CounterDatabase.EnsureDirectoryExists("Data Source=counter.db", null!));
    }
}
