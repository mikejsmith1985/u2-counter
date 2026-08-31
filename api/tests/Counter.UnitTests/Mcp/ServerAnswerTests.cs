namespace Counter.UnitTests.Mcp;

using System.Text.Json;
using Counter.Infrastructure.Mcp;

/// <summary>
/// Telling an answer from a refusal, and a whole answer from part of one.
/// </summary>
/// <remarks>
/// The server reports most failures inside the payload rather than by failing
/// the call, so a caller that only watched for an exception would read "record
/// not found" as a record. That is the first thing here.
///
/// The second matters more. A query answer says whether it is complete, and the
/// absence of that flag means complete -- but only because the server states
/// incompleteness explicitly when it applies. Reading it the other way round
/// would mark every answer partial, which is merely annoying. Reading a partial
/// answer as complete is the dangerous direction: a truncated read of which
/// branches hold a part becomes "no other branch has it", and somebody tells a
/// customer to drive to the wrong one.
/// </remarks>
public sealed class ServerAnswerTests
{
    /// <summary>Parse a JSON literal into a payload.</summary>
    /// <param name="json">The server's answer.</param>
    /// <returns>The parsed element.</returns>
    private static JsonElement Payload(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void An_error_in_the_payload_is_a_missing_record_not_a_record()
    {
        // The call succeeded. Only the body says otherwise, and a caller
        // watching for an exception would take this for a real record.
        ErpRecordNotFoundException missing =
            Assert.Throws<ErpRecordNotFoundException>(() => ErpResponse.RawRecordFrom(
                Payload("""{"error":"Record not found","file":"BRANCH","id":"XXX"}"""),
                "BRANCH",
                "XXX"));

        Assert.Contains("XXX", missing.Message, StringComparison.Ordinal);
        Assert.Contains("BRANCH", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_raw_record_is_preferred_where_the_server_sent_one()
    {
        // It is what the record view shows as the stored form, and it is the
        // server's own bytes rather than a reconstruction of them.
        string raw = ErpResponse.RawRecordFrom(
            Payload("""{"raw":"AuroraþAUR","fields":{"1":"something else"}}"""),
            "BRANCH",
            "AUR");

        Assert.Equal("AuroraþAUR", raw);
    }

    [Fact]
    public void A_record_with_no_raw_form_is_rebuilt_from_its_fields()
    {
        // Not every tool returns the raw form, and the alternative to
        // rebuilding is showing nothing.
        string raw = ErpResponse.RawRecordFrom(
            Payload("""{"fields":{"1":"Aurora","2":"AUR"}}"""),
            "BRANCH",
            "AUR");

        Assert.Equal("AuroraþAUR", raw);
    }

    [Fact]
    public void An_answer_that_does_not_mention_completeness_is_complete()
    {
        // The server says so only when it is not. Defaulting the other way
        // would mark every answer partial and train somebody to ignore the
        // warning that matters.
        ErpQueryResult result = ErpResponse.QueryResultFrom(
            Payload("""{"output":"12 records listed"}"""), "LIST BRANCH");

        Assert.True(result.IsComplete);
        Assert.Equal("12 records listed", result.Output);
    }

    [Fact]
    public void An_answer_that_says_it_is_incomplete_is_not_reported_as_whole()
    {
        // The dangerous direction. A truncated read of which branches hold a
        // part, reported as complete, becomes "no other branch has it".
        ErpQueryResult result = ErpResponse.QueryResultFrom(
            Payload("""{"output":"first 50 of many","is_complete":false}"""),
            "LIST INVENTORY");

        Assert.False(result.IsComplete);
    }

    [Fact]
    public void A_warning_is_carried_through_rather_than_dropped()
    {
        // It is the sentence that explains a short answer, and dropping it
        // leaves the answer looking merely small.
        ErpQueryResult result = ErpResponse.QueryResultFrom(
            Payload("""{"output":"some","is_complete":false,"warning":"stopped at 50"}"""),
            "LIST INVENTORY");

        Assert.Equal("stopped at 50", result.Warning);
    }

    [Fact]
    public void A_refused_query_names_the_query_that_was_refused()
    {
        // So the message can be read without going to find what was asked.
        ErpRefusedException refused = Assert.Throws<ErpRefusedException>(() =>
            ErpResponse.QueryResultFrom(
                Payload("""{"error":"Syntax error near WITH"}"""),
                "LIST BRANCH WITH"));

        Assert.Contains("LIST BRANCH WITH", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Syntax error", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_with_no_output_is_empty_rather_than_a_failure()
    {
        // A query matching nothing is an ordinary result.
        ErpQueryResult result = ErpResponse.QueryResultFrom(
            Payload("""{"is_complete":true}"""), "LIST BRANCH WITH NAME = \"nothing\"");

        Assert.Equal(string.Empty, result.Output);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void A_warning_that_is_not_a_sentence_is_treated_as_absent()
    {
        // A null or a number where a message was expected must not become the
        // string "null" on somebody's screen.
        ErpQueryResult result = ErpResponse.QueryResultFrom(
            Payload("""{"output":"some","warning":null}"""), "LIST BRANCH");

        Assert.Null(result.Warning);
    }
}
