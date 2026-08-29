namespace Counter.Api.Services;

using System.Collections.Concurrent;

/// <summary>
/// Who each browser is signed in as, and which customer they are serving.
/// </summary>
/// <remarks>
/// Held in memory: this demonstration has no identity provider and no reason to
/// persist a session beyond the process. The selected customer lives here rather
/// than in the browser so it survives a reload, because a customer is fixed for
/// the length of a call while the parts they ask about are not.
/// </remarks>
public sealed class SessionStore
{
    /// <summary>The cookie carrying the session key.</summary>
    private const string CookieName = "counter.session";

    /// <summary>How long a session lasts without use.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    private readonly ConcurrentDictionary<string, CounterSession> _sessions = new();

    /// <summary>
    /// Return the session for this request, creating one if there is none.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <remarks>
    /// An unsigned-in visitor gets the first persona rather than an error. This
    /// is a demonstration whose point is the counter screen; making someone
    /// choose an identity before they can look at anything would put a form in
    /// front of the thing they came to see.
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

        return updated;
    }

    /// <summary>
    /// Read the session key from the request, setting a cookie if there is none.
    /// </summary>
    private static string KeyFor(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(CookieName, out string? existing) &&
            !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        string key = Guid.NewGuid().ToString("N");

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
