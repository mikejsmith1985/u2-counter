namespace Counter.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The two things this application stores: who is signed in, and what they did.
/// </summary>
/// <remarks>
/// Nothing about the ERP is stored here. Every part, price and stock figure is
/// read live and thrown away, which is what lets the read-only claim be checked
/// by comparing the ERP files before and after a run rather than argued about.
///
/// What is stored is the record of asking. That has to outlive the process: an
/// audit trail held in memory answers "what did I just do" and nothing a reviewer
/// would ever ask.
/// </remarks>
/// <param name="options">Provider and connection, supplied by configuration.</param>
public sealed class CounterContext(DbContextOptions<CounterContext> options) : DbContext(options)
{
    /// <summary>Who each browser is signed in as.</summary>
    public DbSet<UserSessionRow> Sessions => Set<UserSessionRow>();

    /// <summary>One row per request, successes and failures alike.</summary>
    public DbSet<ActivityRow> Activity => Set<ActivityRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Applied to every instant in the model rather than to each one by hand.
        // A column that missed this would compare and order wrongly, and would do
        // it silently -- which is the failure worth removing the opportunity for.
        InstantConverter instants = new();

        modelBuilder.Entity<UserSessionRow>(entity =>
        {
            entity.ToTable("UserSession");
            entity.HasKey(row => row.SessionKey);
            entity.Property(row => row.SessionKey).HasMaxLength(64);
            entity.Property(row => row.UserSubject).HasMaxLength(128).IsRequired();
            entity.Property(row => row.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(row => row.BranchCode).HasMaxLength(8).IsRequired();
            entity.Property(row => row.SelectedCustomerAccount).HasMaxLength(32);

            // Expiry is queried on every request to sweep dead sessions, and a
            // scan of the table to find them would grow with the demonstration.
            entity.HasIndex(row => row.ExpiresAt);

            entity.Property(row => row.StartedAt).HasConversion(instants);
            entity.Property(row => row.ExpiresAt).HasConversion(instants);
        });

        modelBuilder.Entity<ActivityRow>(entity =>
        {
            entity.ToTable("ActivityRecord");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.UserSubject).HasMaxLength(128).IsRequired();
            entity.Property(row => row.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(row => row.Action).HasMaxLength(64).IsRequired();
            entity.Property(row => row.TargetKey).HasMaxLength(128).IsRequired();
            entity.Property(row => row.DatabaseLogin).HasMaxLength(128).IsRequired();
            entity.Property(row => row.Outcome).HasMaxLength(32).IsRequired();

            // The activity panel asks one question -- what did this person do,
            // most recent first -- so the index answers exactly that question.
            entity.HasIndex(row => new { row.UserSubject, row.OccurredAt })
                .IsDescending(false, true);

            // The index above only means anything if the database can order the
            // column. SQLite stores an offset as text in a format it refuses to
            // compare, so the text is chosen here instead -- see InstantConverter.
            entity.Property(row => row.OccurredAt).HasConversion(instants);
        });
    }
}

/// <summary>
/// One browser's session, stored so it survives a restart of the application.
/// </summary>
/// <remarks>
/// No credential is stored. The session names a persona and the customer being
/// served; the database login it acts under is configuration, not a secret held
/// per user, and its password never reaches this process at all.
/// </remarks>
public sealed class UserSessionRow
{
    /// <summary>The opaque key carried in the session cookie.</summary>
    public required string SessionKey { get; set; }

    /// <summary>Who they are, as an identity provider would name them.</summary>
    public required string UserSubject { get; set; }

    /// <summary>Who they are, as a person would recognise them.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Which branch they answer the phone at.</summary>
    public required string BranchCode { get; set; }

    /// <summary>Whose price to quote, or null for list price.</summary>
    public string? SelectedCustomerAccount { get; set; }

    /// <summary>When the session was opened.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When it stops being usable.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// One recorded request.
/// </summary>
/// <remarks>
/// Deliberately flat, and deliberately without a foreign key to the session.
/// A session is swept when it expires; the record of what was done under it must
/// outlive that, because the reason anyone reads this table is to ask about
/// something that happened a while ago.
/// </remarks>
public sealed class ActivityRow
{
    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>When the request was answered.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Who made it.</summary>
    public required string UserSubject { get; set; }

    /// <summary>Who made it, in words.</summary>
    public required string DisplayName { get; set; }

    /// <summary>What was asked for.</summary>
    public required string Action { get; set; }

    /// <summary>What it was asked about.</summary>
    public required string TargetKey { get; set; }

    /// <summary>Which database account served it.</summary>
    public required string DatabaseLogin { get; set; }

    /// <summary>Whether that account is used by more than one person.</summary>
    public bool DatabaseLoginIsShared { get; set; }

    /// <summary>How long it took.</summary>
    public int DurationMs { get; set; }

    /// <summary>Success, NotFound, Unreachable, Refused or Failed.</summary>
    public required string Outcome { get; set; }
}
