namespace Counter.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Builds a context for the migration tools, which have no running application.
/// </summary>
/// <remarks>
/// The connection string here is never used to reach a server. Creating a
/// migration needs only the provider, so that it can generate SQL Server syntax;
/// applying one is the application's job at startup, against the connection it
/// was actually configured with.
///
/// A placeholder is used deliberately rather than a real address, so nothing a
/// developer runs locally can touch a real database by accident.
/// </remarks>
public sealed class CounterContextFactory : IDesignTimeDbContextFactory<CounterContext>
{
    /// <inheritdoc />
    public CounterContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<CounterContext> options = new();
        options.UseSqlServer("Server=(design-time);Database=Counter;Trusted_Connection=True;");

        return new CounterContext(options.Options);
    }
}
