namespace Counter.UnitTests.Ai;

using Counter.Infrastructure.Ai;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// The four ways the assistant declines before it spends anything.
/// </summary>
/// <remarks>
/// Each of these returns before a single call is made, and that is the point:
/// they are the guards, and a guard that lets one through costs real money on a
/// personal key. They are also the states a visitor is most likely to meet,
/// because meeting them for real means exhausting a budget first -- which is
/// exactly why they had no test.
///
/// The refusals are deliberately four rather than one. "Come back tomorrow",
/// "wait, you have asked a lot", "this was never switched on" and "you did not
/// type anything" want four different replies; collapsing them would leave
/// somebody waiting for a limit that is never going to lift.
/// </remarks>
public sealed class AskRefusalTests
{
    /// <summary>A clock that does not move.</summary>
    /// <param name="now">The moment it reports.</param>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// An assistant that would answer, if it got that far.
    /// </summary>
    /// <param name="options">The limits to apply.</param>
    /// <param name="ledger">What has been spent, or a fresh ledger.</param>
    /// <returns>The service under test.</returns>
    /// <remarks>
    /// Every dependency is real and none of them is reachable: each refusal
    /// returns above the first call. If one of these tests ever fails by
    /// throwing rather than by asserting, a guard has stopped guarding.
    /// </remarks>
    private static AskService Build(AskOptions options, SpendLedger? ledger = null)
    {
        InMemoryErp erp = new([]);

        CatalogueProjection catalogue = new(erp, NullLogger<CatalogueProjection>.Instance);

        return new AskService(
            catalogue,
            new AvailabilityReader(erp, NullLogger<AvailabilityReader>.Instance),
            erp,
            new PriceComparison(
                catalogue,
                new CustomerDirectory(erp, NullLogger<CustomerDirectory>.Instance),
                new PricingReader(erp),
                new FixedClock(DateTimeOffset.UnixEpoch)),
            ledger ?? new SpendLedger(new FixedClock(DateTimeOffset.UnixEpoch)),
            options,
            NullLogger<AskService>.Instance);
    }

    /// <summary>Limits for an assistant that would answer.</summary>
    /// <param name="isConfigured">Whether a key is present.</param>
    /// <param name="questionsPerSession">The session allowance.</param>
    /// <param name="tokensPerDay">The day's ceiling.</param>
    /// <returns>The options.</returns>
    private static AskOptions Limits(
        bool isConfigured = true,
        int questionsPerSession = 12,
        int tokensPerDay = 200_000) =>
        new()
        {
            IsConfigured = isConfigured,
            QuestionsPerSession = questionsPerSession,
            TokensPerDay = tokensPerDay,
        };

    [Fact]
    public async Task A_deployment_with_no_key_says_so_rather_than_failing()
    {
        // Running without an assistant is a supported state. Everything else on
        // the screen works, and the message says that.
        AskService ask = Build(Limits(isConfigured: false));

        (AskResult? result, AskRefusal refusal) = await ask.AnswerAsync(
            "anything at all", ScreenContext.Empty, 0, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(AskRefusal.NotConfigured, refusal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task An_empty_question_is_not_a_question(string question)
    {
        // Checked before the allowance, so a stray submit cannot spend one of
        // the twelve on nothing.
        AskService ask = Build(Limits());

        (AskResult? result, AskRefusal refusal) = await ask.AnswerAsync(
            question, ScreenContext.Empty, 0, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(AskRefusal.NothingAsked, refusal);
    }

    [Fact]
    public async Task The_session_allowance_refuses_the_question_after_the_last_one()
    {
        AskService ask = Build(Limits(questionsPerSession: 3));

        (_, AskRefusal refusal) = await ask.AnswerAsync(
            "the fourth question", ScreenContext.Empty, 3, CancellationToken.None);

        Assert.Equal(AskRefusal.SessionLimitReached, refusal);
    }

    [Fact]
    public async Task The_last_question_within_the_allowance_is_not_refused()
    {
        // The boundary in the other direction. An off-by-one here would take a
        // question away from every visitor, silently.
        AskService ask = Build(Limits(questionsPerSession: 3));

        // It gets past every guard and goes on to call out, which cannot
        // succeed here -- reaching that point is the assertion.
        await Assert.ThrowsAnyAsync<Exception>(() => ask.AnswerAsync(
            "the third question", ScreenContext.Empty, 2, CancellationToken.None));
    }

    [Fact]
    public async Task The_day_s_ceiling_refuses_everybody_not_just_the_heavy_user()
    {
        // The backstop that does not depend on anyone behaving well. The
        // session allowance stops one person; this stops a hundred, and one
        // person clearing their cookies.
        SpendLedger spent = new(new FixedClock(DateTimeOffset.UnixEpoch));
        spent.Record(500_000);

        AskService ask = Build(Limits(tokensPerDay: 200_000), spent);

        (_, AskRefusal refusal) = await ask.AnswerAsync(
            "a first question from a brand new visitor",
            ScreenContext.Empty,
            0,
            CancellationToken.None);

        Assert.Equal(AskRefusal.DailyLimitReached, refusal);
    }

    [Fact]
    public async Task Not_being_configured_is_reported_before_anything_else()
    {
        // Order matters in the message it produces. A deployment with no key
        // that also happens to be over its allowance should say the key is
        // missing, because that is the one somebody can act on.
        AskService ask = Build(Limits(isConfigured: false, questionsPerSession: 0));

        (_, AskRefusal refusal) = await ask.AnswerAsync(
            "anything", ScreenContext.Empty, 99, CancellationToken.None);

        Assert.Equal(AskRefusal.NotConfigured, refusal);
    }
}
