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
/// </remarks>
public sealed class SecretRedactionTests
{
    /// <summary>Build a redactor holding the given secrets.</summary>
    /// <param name="settings">Configuration keys and their values.</param>
    /// <returns>The redactor.</returns>
    private static SecretRedactor Holding(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(pair =>
                new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build());

    [Fact]
    public void A_configured_password_is_replaced_wherever_it_appears()
    {
        SecretRedactor redactor = Holding(("U2_PASSWORD", "hunter2secret"));

        Assert.Equal(
            $"login failed for {SecretRedactor.Marker}",
            redactor.Redact("login failed for hunter2secret"));
    }

    [Fact]
    public void The_password_inside_a_connection_string_is_a_secret_of_its_own()
    {
        // The whole string is registered and so is the password inside it,
        // because the password is the part somebody pastes by accident -- the
        // connection string is not what ends up in a search box.
        SecretRedactor redactor = Holding(
            ("ConnectionStrings:Counter", "Data Source=db;User=sa;Password=s3cr3tvalue;"));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact("s3cr3tvalue"));
    }

    [Fact]
    public void The_whole_connection_string_is_redacted_too()
    {
        const string connection = "Data Source=db;User=sa;Password=s3cr3tvalue;";

        SecretRedactor redactor = Holding(("ConnectionStrings:Counter", connection));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact(connection));
    }

    [Fact]
    public void Pwd_is_recognised_as_a_password_as_well_as_Password()
    {
        // Both spellings are ordinary in connection strings, and a redactor that
        // knew only one would be silently useless against half of them.
        SecretRedactor redactor = Holding(
            ("ConnectionStrings:Counter", "Server=db;Uid=sa;Pwd=alsosecret;"));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact("alsosecret"));
    }

    [Fact]
    public void A_longer_secret_is_replaced_whole_rather_than_left_in_pieces()
    {
        // Secrets are tried longest first. Replacing the short one inside the
        // long one would leave the rest of the long one on screen, which is a
        // redaction that reveals the shape and length of what it hid.
        SecretRedactor redactor = Holding(
            ("U2_PASSWORD", "secret"),
            ("U2_MCP_TOKEN", "secretextended"));

        Assert.Equal(SecretRedactor.Marker, redactor.Redact("secretextended"));
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
        SecretRedactor redactor = Holding(("U2_PASSWORD", "hunter2secret"));

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
        SecretRedactor redactor = Holding(("U2_PASSWORD", "hunter2secret"));

        Assert.DoesNotContain(
            "hunter2secret",
            redactor.Redact("prefixhunter2secretsuffix"),
            StringComparison.Ordinal);
    }
}
