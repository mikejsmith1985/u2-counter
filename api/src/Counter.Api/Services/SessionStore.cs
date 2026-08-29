namespace Counter.Api.Services;

using System.Collections.Concurrent;
using Counter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Who each browser is signed in as, and which customer they are serving.
/// </summary>
/// <remarks>
/// The selected customer lives here rather than in the browser so it survives a
/// reload: a customer is fixed for the length of a call, while the parts they ask
/// about are not.
///
/// Sessions are held in memory and written through to the durable store when one
/// is configured, so a restart of the application does not sign everyone out
/// mid-call. Reads never touch the database — the store is the recovery path, not
/// the request path.
///
/// No credential is held. A session names a persona and a customer; the database
/// login is configuration, and its password never reaches this process.
/// </remarks>
/// <param name="contexts">The durable store, absent when none is configured.</param>
/// <param name="logger">For reporting a write-through that could not be made.</param>
public sealed class SessionStore(
    ILogger<SessionStore> logger,
    IDbContextFactory<CounterContext>? contexts = null)
{
    /// <summary>The cookie carrying the session key.</summary>
    private const string CookieName = "counter.session";

    /// <summary>Where a newly issued key is remembered for the rest of a request.</summary>
    private const string KeyItem = "counter.session.key";

    /// <summary>How long a session lasts without use.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly ConcurrentDictionary<string, CounterSession> _sessions = new();
    private readonly IDbContextFactory<CounterContext>? _contexts = contexts;
    private readonly ILogger<SessionStore> _logger = logger;

    /// <summary>Whether sessions survive a restart of the application.</summary>
    public bool IsDurable => _contexts is not null;

    /// <summary>
    /// Return the session for this request, creating one if there is none.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <remarks>
    /// An unsigned-in visitor gets the first persona rather than an error. This is
    /// a demonstration whose point is the counter screen; making someone choose an
    /// identity before they can look at anything would put a form in front of the
    /// thing they came to see.
    /// </remarks>
    public CounterSession ForRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string key = KeyFor(context);

        return _sessions.GetOrAdd(key, _ => new CounterSession(DemonstrationPersonas.All[0], null));
    }

    /// <summary>Sign in as a persona, replacing whatever session existed.</summary>
    /// <param name="context">The request.</param>
    /// <param name="persona">Who to become.</param>
    public CounterSession SignIn(HttpContext context, Persona persona)
    {
        ArgumentNullException.ThrowIfNull(context);

        string key = KeyFor(context);
        CounterSession session = new(persona, null);
        _sessions[key] = session;
        WriteThrough(key, session);

        return session;
    }

    /// <summary>Choose the customer being served.</summary>
    /// <param name="context">The request.</param>
    /// <param name="customerAccount">The account, or null to clear it.</param>
    public CounterSession SelectCustomer(HttpContext context, string? customerAccount)
    {
        ArgumentNullException.ThrowIfNull(context);

        string key = KeyFor(context);
        CounterSession existing = ForRequest(context);
        CounterSession updated = existing with { SelectedCustomerAccount = customerAccount };
        _sessions[key] = updated;
        WriteThrough(key, updated);

        return updated;
    }

    /// <summary>
    /// Reload sessions that were open when the application last stopped.
    /// </summary>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <remarks>
    /// Expired rows are ignored rather than deleted here. Sweeping is a separate
    /// concern from starting up, and a start-up that also writes is a start-up
    /// that can fail for a reason unrelated to starting.
    /// </remarks>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (_contexts is null)
        {
            return;
        }

        await using CounterContext context = await _contexts.CreateDbContextAsync(cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<UserSessionRow> live = await context.Sessions
            .Where(row => row.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (UserSessionRow row in live)
        {
            Persona persona = DemonstrationPersonas.All
                .FirstOrDefault(candidate => candidate.Subject == row.UserSubject)
                ?? DemonstrationPersonas.All[0];

            _sessions[row.SessionKey] = new CounterSession(persona, row.SelectedCustomerAccount);
        }

        _logger.LogInformation("Restored {Count} open sessions", live.Count);
    }

    /// <summary>
    /// Persist a session, without making the request wait for the write.
    /// </summary>
    /// <remarks>
    /// Signing in must not fail because the session store is unavailable: the
    /// person can still be served, and the audit trail — which is the record that
    /// matters — is written separately and does not depend on this row.
    /// </remarks>
    private void WriteThrough(string sessionKey, CounterSession session)
    {
        if (_contexts is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await using CounterContext context =
                    await _contexts.CreateDbContextAsync(CancellationToken.None);

                UserSessionRow? existing = await context.Sessions
                    .FirstOrDefaultAsync(row => row.SessionKey == sessionKey);

                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (existing is null)
                {
                    context.Sessions.Add(new UserSessionRow
                    {
                        SessionKey = sessionKey,
                        UserSubject = session.Persona.Subject,
                        DisplayName = session.Persona.DisplayName,
                        BranchCode = session.Persona.HomeBranchCode,
                        SelectedCustomerAccount = session.SelectedCustomerAccount,
                        StartedAt = now,
                        ExpiresAt = now.Add(Lifetime),
                    });
                }
                else
                {
                    existing.UserSubject = session.Persona.Subject;
                    existing.DisplayName = session.Persona.DisplayName;
                    existing.BranchCode = session.Persona.HomeBranchCode;
                    existing.SelectedCustomerAccount = session.SelectedCustomerAccount;
                    existing.ExpiresAt = now.Add(Lifetime);
                }

                await context.SaveChangesAsync();
            }
#pragma warning disable CA1031 // Persisting a session must never deny service.
            catch (Exception error)
#pragma warning restore CA1031
            {
                _logger.LogError(error, "The session for {Subject} could not be stored",
                    session.Persona.Subject);
            }
        });
    }

    /// <summary>
    /// Read the session key from the request, setting a cookie if there is none.
    /// </summary>
    /// <remarks>
    /// The key issued to a first-time visitor is remembered on the request, so
    /// asking twice in one request gives the same answer.
    ///
    /// This is not a refinement. Without it, a visitor who signs in gets one key
    /// from the sign-in itself and a second from the filter that records the
    /// request afterwards -- two Set-Cookie headers, of which the browser keeps
    /// the last. The person is then signed in under a key their browser no longer
    /// holds, and the next request arrives as a stranger. It looks exactly like
    /// sign-in silently not working.
    /// </remarks>
    private static string KeyFor(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out string? existing) &&
            !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        if (context.Items.TryGetValue(KeyItem, out object? issued) && issued is string alreadyIssued)
        {
            return alreadyIssued;
        }

        string key = Guid.NewGuid().ToString("N");
        context.Items[KeyItem] = key;

        context.Response.Cookies.Append(CookieName, key, new CookieOptions
        {
            // Not readable by script: the key identifies a session, and script
            // has no reason to see it.
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // Secure in production, where the site is served over HTTPS. Left
            // open locally so the demonstration runs over plain HTTP.
            Secure = context.Request.IsHttps,
            MaxAge = Lifetime,
            Path = "/",
        });

        return key;
    }
}

/// <summary>
/// One browser's session.
/// </summary>
/// <param name="Persona">Who they are signed in as.</param>
/// <param name="SelectedCustomerAccount">Whose price to quote, or null for list.</param>
public sealed record CounterSession(Persona Persona, string? SelectedCustomerAccount);
