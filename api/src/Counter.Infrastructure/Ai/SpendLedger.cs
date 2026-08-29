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
/// </remarks>
public sealed class SpendLedger
{
    private readonly object _gate = new();

    private DateOnly _day = DateOnly.FromDateTime(DateTime.UtcNow);
    private int _tokensToday;

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
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (today != _day)
        {
            _day = today;
            _tokensToday = 0;
        }
    }
}
