namespace Counter.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Builds a context for the migration tools, which have no running application.
/// </summary>
/// <remarks>
/// The connection string here never reaches a database. Creating a migration
/// needs only the provider, so that it can generate SQLite syntax; applying one
/// is the application's job at startup, against the file it was configured with.
///
/// A file that cannot exist is named deliberately, so nothing a developer runs
/// locally can touch a real database by accident.
/// </remarks>
public sealed class CounterContextFactory : IDesignTimeDbContextFactory<CounterContext>
{
    /// <inheritdoc />
    public CounterContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CounterContext> options = new();
        options.UseSqlite("Data Source=:design-time:");

        return new CounterContext(options.Options);
    }
}
