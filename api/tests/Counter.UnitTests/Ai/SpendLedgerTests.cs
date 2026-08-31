namespace Counter.UnitTests.Ai;

using Counter.Infrastructure.Ai;

/// <summary>
/// The ceiling on what the assistant may spend in a day.
/// </summary>
/// <remarks>
/// This is the backstop that does not depend on anyone behaving well. The
/// per-session allowance stops one person asking too much; it does nothing
/// about a hundred people, or one person clearing their cookies. Behind it is
/// a personal API key, so the failure mode is a bill rather than an error page.
///
/// It had no test at all, and the roll-over is the part most worth one: a
/// roll-over that never fires switches the assistant off for good after a
/// single busy day, and one that fires on every call means there is no ceiling.
/// Neither shows up as a failure anywhere — the first looks like the limit
/// working, the second looks like nothing at all.
/// </remarks>
public sealed class SpendLedgerTests
{
    /// <summary>A day's ceiling, for these tests.</summary>
    private const int Allowance = 1_000;

    /// <summary>A clock a test can move.</summary>
    /// <param name="start">Where it begins.</param>
    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        /// <summary>Move the clock forward.</summary>
        /// <param name="span">How far.</param>
        public void Advance(TimeSpan span) => _now += span;

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Noon, so a day's advance cannot straddle a boundary by accident.</summary>
    private static MovableClock AtNoon() =>
        new(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_fresh_ledger_has_spent_nothing_and_has_budget()
    {
        SpendLedger ledger = new(AtNoon());

        Assert.Equal(0, ledger.TokensToday);
        Assert.True(ledger.HasBudgetToday(Allowance));
    }

    [Fact]
    public void Spending_accumulates()
    {
        SpendLedger ledger = new(AtNoon());

        ledger.Record(400);
        ledger.Record(350);

        Assert.Equal(750, ledger.TokensToday);
        Assert.True(ledger.HasBudgetToday(Allowance));
    }

    [Fact]
    public void Reaching_the_ceiling_refuses_the_next_question()
    {
        SpendLedger ledger = new(AtNoon());

        ledger.Record(Allowance);

        Assert.False(ledger.HasBudgetToday(Allowance));
    }

    [Fact]
    public void The_ceiling_may_be_passed_by_one_question_and_no_more()
    {
        // Cost is recorded after the answer rather than reserved before it,
        // because the size of an answer is not known until it exists. So the
        // ceiling is crossed once, by whatever that last answer cost, and the
        // question after it is refused. Pinned because it is a deliberate
        // choice that reads like an off-by-one.
        SpendLedger ledger = new(AtNoon());

        ledger.Record(Allowance - 1);
        Assert.True(ledger.HasBudgetToday(Allowance));

        ledger.Record(5_000);
        Assert.False(ledger.HasBudgetToday(Allowance));
    }

    [Fact]
    public void A_new_day_starts_the_count_again()
    {
        // Without this the assistant is switched off for good after one busy
        // day, which looks exactly like the limit working correctly.
        MovableClock clock = AtNoon();
        SpendLedger ledger = new(clock);

        ledger.Record(Allowance);
        Assert.False(ledger.HasBudgetToday(Allowance));

        clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal(0, ledger.TokensToday);
        Assert.True(ledger.HasBudgetToday(Allowance));
    }

    [Fact]
    public void The_count_survives_the_hours_within_one_day()
    {
        // The failure in the other direction: a roll-over that fires whenever
        // the clock moves is no ceiling at all, and nothing about it looks wrong.
        MovableClock clock = AtNoon();
        SpendLedger ledger = new(clock);

        ledger.Record(600);
        clock.Advance(TimeSpan.FromHours(6));
        ledger.Record(300);

        Assert.Equal(900, ledger.TokensToday);
    }

    [Fact]
    public void A_ledger_built_before_midnight_and_first_used_after_it_is_not_a_day_behind()
    {
        // It is a singleton, built when the application starts and possibly
        // first used hours later. Taking the day in the constructor would make
        // that first use look like a new day and reset a count of zero, which
        // is harmless — but the same reasoning applied to a restart mid-day is
        // not, so the day is established on first use.
        MovableClock clock = new(new DateTimeOffset(2026, 8, 31, 23, 59, 0, TimeSpan.Zero));
        SpendLedger ledger = new(clock);

        clock.Advance(TimeSpan.FromMinutes(2));
        ledger.Record(400);

        Assert.Equal(400, ledger.TokensToday);
    }

    [Fact]
    public void A_negative_cost_cannot_buy_back_budget()
    {
        // Nothing should ever report one, which is the reason to be sure: a
        // token count arriving negative from a malformed response would
        // otherwise refund the day's spending.
        SpendLedger ledger = new(AtNoon());

        ledger.Record(900);
        ledger.Record(-500);

        Assert.Equal(900, ledger.TokensToday);
    }

    [Fact]
    public void Concurrent_recording_loses_nothing()
    {
        // It is a singleton behind a web server, so every request touches it.
        SpendLedger ledger = new(AtNoon());

        Parallel.For(0, 500, _ => ledger.Record(10));

        Assert.Equal(5_000, ledger.TokensToday);
    }
}
