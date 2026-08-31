namespace Counter.Infrastructure.Ai;

/// <summary>
/// What the assistant has spent today, and whether it may spend more.
/// </summary>
/// <remarks>
/// The backstop that does not depend on anyone behaving well. Per-session limits
/// stop one person asking too much; they do nothing about a hundred people, or
/// about one person clearing their cookies. This is the ceiling on the whole
/// deployment, and the key behind it is a personal one.
///
/// Held in memory rather than in the database, deliberately. The application
/// scales to zero, so a restart resets the count — which sounds like a hole and
/// is not one worth closing here: a restart costs a cold start, so anyone trying
/// to reset the counter on purpose is rate-limited by the platform to a handful
/// of attempts an hour. Storing it durably would mean an audit database write on
/// the request path for a limit that exists to prevent a bill of a few dollars.
///
/// Written down because the reasoning is the sort that looks like an oversight.
///
/// The clock arrives rather than being read from the ambient one, so the
/// roll-over can be exercised without waiting for midnight. It is the part
/// most worth testing and was the one part untestable: a roll-over that never
/// fires leaves the assistant switched off after a single busy day, and one
/// that fires constantly means no ceiling at all.
/// </remarks>
/// <param name="clock">The clock, so a test need not wait for midnight.</param>
public sealed class SpendLedger(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock;

    private DateOnly _day;
    private int _tokensToday;

    /// <summary>The day the count belongs to, read on first use.</summary>
    private bool _hasStarted;

    /// <summary>How many tokens have been spent today.</summary>
    public int TokensToday
    {
        get
        {
            lock (_gate)
            {
                RollOverIfNewDay();
                return _tokensToday;
            }
        }
    }

    /// <summary>
    /// Whether there is budget left today.
    /// </summary>
    /// <param name="allowance">The day's ceiling.</param>
    /// <returns>True while the ceiling has not been reached.</returns>
    public bool HasBudgetToday(int allowance)
    {
        lock (_gate)
        {
            RollOverIfNewDay();
            return _tokensToday < allowance;
        }
    }

    /// <summary>
    /// Record what an answer cost.
    /// </summary>
    /// <param name="tokens">Input and output tokens together.</param>
    /// <remarks>
    /// Recorded after the answer rather than reserved before it, so the ceiling
    /// can be passed by at most one question. That is the right way round: a
    /// question already asked is answered, and the next one is refused.
    /// </remarks>
    public void Record(int tokens)
    {
        lock (_gate)
        {
            RollOverIfNewDay();
            _tokensToday += Math.Max(0, tokens);
        }
    }

    /// <summary>Start a fresh count when the date changes. Caller holds the lock.</summary>
    private void RollOverIfNewDay()
    {
        DateOnly today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

        if (!_hasStarted)
        {
            // The first read establishes the day rather than the constructor,
            // because a singleton built at start-up and first used after
            // midnight would otherwise begin one day behind.
            _hasStarted = true;
            _day = today;
            return;
        }

        if (today != _day)
        {
            _day = today;
            _tokensToday = 0;
        }
    }
}
