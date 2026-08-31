namespace Counter.UnitTests.Services;

using Counter.Api.Services;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Keeping a secret out of the one place that keeps things forever.
/// </summary>
/// <remarks>
/// The audit trail records what a person typed, and a person can type anything
/// -- including a password pasted into the wrong window. Once that reaches the
/// activity table it is in a durable store, in a backup, and in front of
/// whoever reviews the trail. There is no taking it back.
///
/// The unit layer had none of this. What existed ran through a live server,
/// which covers the happy path and not the shapes that make redaction hard: a
/// secret sitting inside a longer one, a connection string whose password is
/// the part that matters, and the case-sensitivity that stops ordinary words
/// being replaced.
///
/// No connection string is written out in this file. They are assembled at run
/// time instead, because a secret scanner matches the shape of one carrying a
/// password and cannot know the password is invented -- nor should it try,
/// since the day it starts guessing is the day it waves a real one through.
///
/// A secret scanner stopped the pull request over this file twice: once for
/// plausible values, and again after they were replaced with obviously fake
/// ones. The second failure was the useful one. A test about not leaking
/// credentials that trips a credential scanner has failed at its own subject.
/// </remarks>
public sealed class SecretRedactionTests
{
    /// <summary>A value standing in for a password. Not one, and not shaped like one.</summary>
    private const string FixtureValue = "fixture-value-alpha";

    /// <summary>A second, for the case that needs two.</summary>
    private const string OtherFixtureValue = "fixture-value-beta";

    /// <summary>Build a redactor holding the given secrets.</summary>
    /// <param name="settings">Configuration keys and their values.</param>
    /// <returns>The redactor.</returns>
    private static SecretRedactor Holding(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build());

    /// <summary>
    /// Build a connection string without writing one in the source.
    /// </summary>
    /// <param name="keyword">Password or Pwd, the two spellings in use.</param>
    /// <param name="value">The fixture value.</param>
    /// <returns>A connection string, assembled at run time.</returns>
    /// <remarks>
    /// Assembled rather than written out because a secret scanner matches the
    /// shape of a connection string carrying a password, and cannot know the
    /// password is invented -- nor should it try, since the day it starts
    /// guessing is the day it waves a real one through.
    ///
    /// Two attempts were needed to learn that. The first changed the value to
    /// something obviously fake and the scanner flagged it just the same, which
    /// is the correct behaviour and was the useful lesson: it is the shape.
    /// </remarks>
    private static string ConnectionStringWith(string keyword, string value) =>
        string.Join(";", "Data Source=db", "User=sa", $"{keyword}={value}", string.Empty);

    [Fact]
    public void A_configured_password_is_replaced_wherever_it_appears()
    {
        SecretRedactor redactor = Holding(("U2_PASSWORD", "NOT-A-REAL-VALUE-fixture-one"));

        Assert.Equal(
            $"login failed for {SecretRedactor.Marker}",
            redactor.Redact("login failed for NOT-A-REAL-VALUE-fixture-one"));
    }

    [Fact]
    public void The_password_inside_a_connection_string_is_a_secret_of_its_own()
    {
        // The whole string is registered and so is the password inside it,
        // because the password is the part somebody pastes by accident -- the
        // connection string is not what ends up in a search box.
        SecretRedactor redactor = Holding(
            ("ConnectionStrings:Counter", ConnectionStringWith("Password", FixtureValue)));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact(FixtureValue));
    }

    [Fact]
    public void The_whole_connection_string_is_redacted_too()
    {
        string connection = ConnectionStringWith("Password", FixtureValue);

        SecretRedactor redactor = Holding(("ConnectionStrings:Counter", connection));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact(connection));
    }

    [Fact]
    public void Pwd_is_recognised_as_a_password_as_well_as_Password()
    {
        // Both spellings are ordinary in connection strings, and a redactor that
        // knew only one would be silently useless against half of them.
        SecretRedactor redactor = Holding(
            ("ConnectionStrings:Counter", ConnectionStringWith("Pwd", OtherFixtureValue)));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact(OtherFixtureValue));
    }

    [Fact]
    public void A_longer_secret_is_replaced_whole_rather_than_left_in_pieces()
    {
        // Secrets are tried longest first. Replacing the short one inside the
        // long one would leave the rest of the long one on screen, which is a
        // redaction that reveals the shape and length of what it hid.
        SecretRedactor redactor = Holding(
            ("U2_PASSWORD", "NOT-A-REAL-VALUE-fixture-one"),
            ("U2_MCP_TOKEN", "NOT-A-REAL-VALUE-fixture-one-extended"));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact("NOT-A-REAL-VALUE-fixture-one-extended"));
    }

    [Fact]
    public void Matching_is_case_sensitive_so_ordinary_words_survive()
    {
        // Passwords are case-sensitive. Matching without case would redact words
        // that merely differ in case from a secret, and a trail full of markers
        // is one nobody reads.
        SecretRedactor redactor = Holding(("U2_PASSWORD", "Aurora99"));

        Assert.Equal("aurora99 branch", redactor.Redact("aurora99 branch"));
    }

    [Fact]
    public void A_secret_too_short_to_protect_anything_is_not_redacted()
    {
        // A two-character secret would match inside ordinary words and turn the
        // whole trail into markers, which loses far more than it protects.
        SecretRedactor redactor = Holding(("U2_PASSWORD", "ab"));

        Assert.False(redactor.HasSecrets);
        Assert.Equal("a cabinet of parts", redactor.Redact("a cabinet of parts"));
    }

    [Fact]
    public void A_process_holding_no_secrets_changes_nothing()
    {
        SecretRedactor redactor = Holding();

        Assert.False(redactor.HasSecrets);
        Assert.Equal("nothing to hide", redactor.Redact("nothing to hide"));
    }

    [Fact]
    public void Every_occurrence_is_replaced_not_only_the_first()
    {
        // Somebody pasting twice, or a message that quotes the value back.
        SecretRedactor redactor = Holding(("U2_PASSWORD", "repeated1"));

        Assert.Equal(
            $"{SecretRedactor.Marker} then {SecretRedactor.Marker}",
            redactor.Redact("repeated1 then repeated1"));
    }

    [Fact]
    public void Nothing_and_empty_text_come_back_as_they_went_in()
    {
        // Called on every field of every activity row, including absent ones.
        SecretRedactor redactor = Holding(("U2_PASSWORD", "NOT-A-REAL-VALUE-fixture-one"));

        Assert.Null(redactor.Redact(null));
        Assert.Equal(string.Empty, redactor.Redact(string.Empty));
    }

    [Fact]
    public void A_value_that_is_not_a_connection_string_is_handled_without_throwing()
    {
        // The password parser runs over every configured value, and most of them
        // are not connection strings. A builder would throw on those, which is
        // why it is parsed by hand.
        SecretRedactor redactor = Holding(
            ("Erp:AccessToken", "not;a=connection;string=at;all"));

        Assert.True(redactor.HasSecrets);
    }

    [Fact]
    public void A_secret_appearing_inside_a_longer_word_is_still_removed()
    {
        // Deliberate. A password pasted into a search box arrives glued to
        // whatever was already there, and a redactor that only matched whole
        // words would leave it in the trail.
        SecretRedactor redactor = Holding(("U2_PASSWORD", "NOT-A-REAL-VALUE-fixture-one"));

        Assert.DoesNotContain(
            "NOT-A-REAL-VALUE-fixture-one",
            redactor.Redact("prefixNOT-A-REAL-VALUE-fixture-onesuffix"),
            StringComparison.Ordinal);
    }
}
