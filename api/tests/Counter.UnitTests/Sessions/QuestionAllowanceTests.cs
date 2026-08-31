namespace Counter.UnitTests.Sessions;

using Counter.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Whose questions count against whose allowance.
/// </summary>
/// <remarks>
/// The assistant allows twelve questions per session, because it runs on a
/// personal API key. The count was kept against the persona rather than the
/// session, and every visitor who has not chosen a persona is the same one --
/// so it was a single bucket shared by everybody using the deployment at once.
///
/// Two people trying it together would have spent each other's questions, and
/// whoever asked the twelfth would have switched the assistant off for the
/// other with no way to get it back. Nothing failed: the count went up, the
/// refusal was correct and well worded, and it was refusing the wrong person.
///
/// Found by an evaluation harness that had no cookie jar and so, without
/// meaning to, made every question look like it came from one visitor.
/// </remarks>
public sealed class QuestionAllowanceTests
{
    /// <summary>A store with no database behind it. Nothing here is persisted.</summary>
    /// <returns>The store under test.</returns>
    private static SessionStore NewStore() =>
        new(NullLogger<SessionStore>.Instance);

    /// <summary>A request carrying a session cookie, or none at all.</summary>
    /// <param name="sessionKey">The cookie value, or null for a first visit.</param>
    /// <returns>The request context.</returns>
    private static DefaultHttpContext RequestFrom(string? sessionKey)
    {
        DefaultHttpContext context = new();

        if (sessionKey is not null)
        {
            context.Request.Headers.Cookie = $"counter.session={sessionKey}";
        }

        return context;
    }

    [Fact]
    public void Two_visitors_do_not_spend_each_others_allowance()
    {
        // The defect, stated plainly.
        SessionStore store = NewStore();

        DefaultHttpContext oneVisitor = RequestFrom("one-visitor");
        DefaultHttpContext anotherVisitor = RequestFrom("another-visitor");

        store.RecordQuestion(oneVisitor);
        store.RecordQuestion(oneVisitor);
        store.RecordQuestion(oneVisitor);

        Assert.Equal(3, store.QuestionsAsked(oneVisitor));
        Assert.Equal(0, store.QuestionsAsked(anotherVisitor));
    }

    [Fact]
    public void A_visitor_asking_again_counts_against_the_same_allowance()
    {
        // The other half. Keying on something unique per request would make the
        // allowance unenforceable, which is the failure in the other direction.
        SessionStore store = NewStore();

        store.RecordQuestion(RequestFrom("one-visitor"));
        store.RecordQuestion(RequestFrom("one-visitor"));

        Assert.Equal(2, store.QuestionsAsked(RequestFrom("one-visitor")));
    }

    [Fact]
    public void Signing_in_as_a_persona_does_not_hand_over_its_spent_questions()
    {
        // Personas are shared: the deployment offers the same handful to
        // everybody. If the count followed the persona, choosing one would
        // inherit whatever the last visitor to choose it had spent.
        SessionStore store = NewStore();

        DefaultHttpContext first = RequestFrom("first-visitor");
        store.SignIn(first, DemonstrationPersonas.All[0]);
        store.RecordQuestion(first);
        store.RecordQuestion(first);

        DefaultHttpContext second = RequestFrom("second-visitor");
        store.SignIn(second, DemonstrationPersonas.All[0]);

        Assert.Equal(0, store.QuestionsAsked(second));
    }

    [Fact]
    public void A_first_visit_has_spent_nothing()
    {
        // No cookie yet. The key is issued during the request, so the count must
        // start at zero rather than reading whatever an unkeyed bucket holds.
        SessionStore store = NewStore();

        store.RecordQuestion(RequestFrom("someone-already-here"));

        Assert.Equal(0, store.QuestionsAsked(RequestFrom(null)));
    }

    [Fact]
    public void Two_first_visits_are_two_visitors()
    {
        // Each gets its own freshly issued key, so neither sees the other's.
        SessionStore store = NewStore();

        DefaultHttpContext arriving = RequestFrom(null);
        store.RecordQuestion(arriving);

        Assert.Equal(1, store.QuestionsAsked(arriving));
        Assert.Equal(0, store.QuestionsAsked(RequestFrom(null)));
    }
}
