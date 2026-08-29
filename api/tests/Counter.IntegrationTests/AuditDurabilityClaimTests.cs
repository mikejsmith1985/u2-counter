namespace Counter.IntegrationTests;

using System.Text.Json;
using Counter.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Counter.Infrastructure.Data;

/// <summary>
/// The health endpoint may not claim an audit trail it does not have.
/// </summary>
/// <remarks>
/// Found in the deployed application rather than here, which is the point of it.
/// `/health` reported `isAuditDurable: true` while every write was failing with
/// "no such table: ActivityRecord" — the schema had never been created, because
/// SQLite cannot take the write lock it needs on the Azure Files share the
/// database sits on, and the startup migration was swallowed by the handler that
/// keeps a missing audit trail from stopping the application.
///
/// Each of those pieces was deliberate and defensible on its own. Together they
/// produced the one thing this application exists to argue against: a system
/// reporting a compliance property it did not have, in a field built to be
/// read by someone checking exactly that.
///
/// The old claim tested whether a database had been *configured*. What a reviewer
/// is asking is whether anything is being *stored*, and those are different
/// questions with different answers — which is the whole defect in one sentence.
/// </remarks>
public sealed class AuditDurabilityClaimTests
{
    /// <summary>Configuration naming no secrets, which is all these tests need.</summary>
    private static readonly IConfiguration EmptyConfiguration =
        new ConfigurationBuilder().Build();

    /// <summary>A recorder with no durable store configured at all.</summary>
    private static ActivityRecorder WithoutStore() =>
        new(new SecretRedactor(EmptyConfiguration), NullLogger<ActivityRecorder>.Instance);

    /// <summary>One entry, with the shape the filter produces.</summary>
    private static ActivityEntry AnEntry() => new(
        OccurredAt: DateTimeOffset.UtcNow,
        UserSubject: "demo|dana",
        DisplayName: "Dana",
        Action: "Searched",
        TargetKey: "breaker",
        DatabaseLogin: "u2demo@DEMO",
        DatabaseLoginIsShared: true,
        DurationMs: 12,
        Outcome: "Success");

    [Fact]
    public void No_store_configured_is_not_durable()
    {
        Assert.False(WithoutStore().IsDurable);
    }

    [Fact]
    public void A_store_that_cannot_be_opened_is_not_durable()
    {
        // The deployed case. A factory exists and hands out contexts; the
        // database behind it has no schema and never will, because the migration
        // that would have created it could not take a lock.
        ActivityRecorder recorder = WithoutStore();

        recorder.ReportDurableStoreUnavailable();

        Assert.False(recorder.IsDurable);
    }

    [Fact]
    public async Task A_failed_write_withdraws_the_claim()
    {
        // Self-correcting, and deliberately so. The startup report covers a
        // migration that failed; this covers everything that can go wrong
        // afterwards — a share that unmounts, a disk that fills, a lock that
        // cannot be taken on the first row rather than the first table.
        //
        // A claim that is only ever set once is a claim that can outlive its
        // truth, and this one is read by someone deciding whether the trail can
        // be relied on.
        ActivityRecorder recorder = new(
            new SecretRedactor(EmptyConfiguration),
            NullLogger<ActivityRecorder>.Instance,
            new FailingContextFactory());

        Assert.True(recorder.IsDurable);

        await recorder.RecordAsync(AnEntry());

        Assert.False(recorder.IsDurable);
    }

    [Fact]
    public async Task The_entry_is_still_remembered_in_memory_when_the_store_fails()
    {
        // Withdrawing the claim must not cost the activity panel its contents.
        // Degrading is the intended behaviour; going quiet is not.
        ActivityRecorder recorder = new(
            new SecretRedactor(EmptyConfiguration),
            NullLogger<ActivityRecorder>.Instance,
            new FailingContextFactory());

        await recorder.RecordAsync(AnEntry());

        Assert.Single(recorder.Recent("demo|dana", 10));
    }
}

/// <summary>
/// A context factory whose contexts cannot save.
/// </summary>
/// <remarks>
/// Points at a SQLite file in a directory that does not exist, which is the
/// closest local stand-in for the deployed failure: the connection opens and the
/// write is what fails.
/// </remarks>
internal sealed class FailingContextFactory : IDbContextFactory<CounterContext>
{
    /// <inheritdoc />
    public CounterContext CreateDbContext()
    {
        DbContextOptions<CounterContext> options =
            new DbContextOptionsBuilder<CounterContext>()
                .UseSqlite("Data Source=/nonexistent-directory/counter.db")
                .Options;

        return new CounterContext(options);
    }
}
