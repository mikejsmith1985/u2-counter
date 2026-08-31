namespace Counter.UnitTests.Sessions;

using Counter.Api.Services;
using Counter.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Remembering a session, and never letting that get in the way.
/// </summary>
/// <remarks>
/// Sessions are written through to the audit database so that a restart does
/// not sign everybody out -- this deployment scales to zero, so restarts are
/// routine rather than exceptional.
///
/// The rule underneath is that persisting must never deny service. The durable
/// store is optional here; the counter screen is not. A deployment with no
/// database at all has to work, and one whose database has gone away has to
/// keep working rather than start refusing the requests it could still answer.
///
/// Both paths were uncovered, and both are the kind that only show up when
/// something else has already gone wrong -- which is the worst moment to
/// discover that the handling of it was never exercised.
/// </remarks>
public sealed class SessionPersistenceTests
{
    /// <summary>A context factory that cannot produce a context.</summary>
    /// <remarks>
    /// Stands in for the share being unmounted, the file being locked by
    /// another writer, or the disk being full -- all of which this has to
    /// survive without taking the screen down with it.
    /// </remarks>
    private sealed class BrokenContexts : IDbContextFactory<CounterContext>
    {
        /// <inheritdoc />
        public CounterContext CreateDbContext() =>
            throw new InvalidOperationException("The audit database is not reachable.");
    }

    /// <summary>A request carrying a session cookie.</summary>
    /// <param name="sessionKey">The cookie value.</param>
    /// <returns>The request context.</returns>
    private static DefaultHttpContext RequestFrom(string sessionKey)
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Cookie = $"counter.session={sessionKey}";
        return context;
    }

    [Fact]
    public void A_deployment_with_no_database_still_signs_somebody_in()
    {
        // The supported configuration this runs in most often: no connection
        // string, no durable trail, and a working counter screen.
        SessionStore store = new(NullLogger<SessionStore>.Instance);

        CounterSession session = store.SignIn(
            RequestFrom("a-visitor"), DemonstrationPersonas.All[0]);

        Assert.Equal(DemonstrationPersonas.All[0].Subject, session.Persona.Subject);
    }

    [Fact]
    public async Task Restoring_from_a_database_that_is_not_there_is_a_no_op()
    {
        // Called during start-up. Throwing here would mean a deployment that
        // will not start because the optional half of it is absent.
        SessionStore store = new(NullLogger<SessionStore>.Instance);

        await store.RestoreAsync(CancellationToken.None);
    }

    [Fact]
    public void A_database_that_cannot_be_reached_does_not_deny_a_sign_in()
    {
        // The share unmounted, the file locked, the disk full. The session is
        // held in memory either way, so the screen keeps working and the only
        // thing lost is that a restart will sign this person out.
        SessionStore store = new(NullLogger<SessionStore>.Instance, new BrokenContexts());

        CounterSession session = store.SignIn(
            RequestFrom("a-visitor"), DemonstrationPersonas.All[0]);

        Assert.Equal(DemonstrationPersonas.All[0].Subject, session.Persona.Subject);
    }

    [Fact]
    public void A_broken_database_does_not_deny_choosing_a_customer_either()
    {
        // The other write-through, on the path somebody takes mid-call.
        SessionStore store = new(NullLogger<SessionStore>.Instance, new BrokenContexts());

        DefaultHttpContext request = RequestFrom("a-visitor");
        store.SignIn(request, DemonstrationPersonas.All[0]);

        CounterSession session = store.SelectCustomer(request, "C-10002");

        Assert.Equal("C-10002", session.SelectedCustomerAccount);
    }

    [Fact]
    public void The_session_is_still_readable_after_a_failed_write_through()
    {
        // What matters is that the in-memory session is authoritative for the
        // life of the process. If a failed write left it unset, the next
        // request would find a stranger.
        SessionStore store = new(NullLogger<SessionStore>.Instance, new BrokenContexts());

        DefaultHttpContext request = RequestFrom("a-visitor");
        store.SignIn(request, DemonstrationPersonas.All[^1]);

        CounterSession again = store.ForRequest(RequestFrom("a-visitor"));

        Assert.Equal(DemonstrationPersonas.All[^1].Subject, again.Persona.Subject);
    }

    [Fact]
    public void Choosing_a_customer_and_then_clearing_it_leaves_the_persona_alone()
    {
        // Clearing the customer is an ordinary step -- the next call is for
        // somebody else. It must not sign the representative out with it.
        SessionStore store = new(NullLogger<SessionStore>.Instance);

        DefaultHttpContext request = RequestFrom("a-visitor");
        store.SignIn(request, DemonstrationPersonas.All[0]);
        store.SelectCustomer(request, "C-10002");

        CounterSession cleared = store.SelectCustomer(request, null);

        Assert.Null(cleared.SelectedCustomerAccount);
        Assert.Equal(DemonstrationPersonas.All[0].Subject, cleared.Persona.Subject);
    }
}
