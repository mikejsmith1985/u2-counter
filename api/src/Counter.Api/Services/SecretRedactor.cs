namespace Counter.Api.Services;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Removes configured secrets from anything on its way into a stored record.
/// </summary>
/// <remarks>
/// The audit trail stores what a person typed, and a person can type anything —
/// including, by accident, a password pasted into the wrong window. Once that
/// reaches the activity table it is in a durable store, in a backup, and in
/// front of whoever reviews the trail.
///
/// So every value this process holds as a secret is replaced before a record is
/// written. The list is built from configuration at startup rather than from a
/// pattern, because a pattern that recognises passwords in general does not
/// exist, while the specific strings this process must never write down are
/// known exactly.
///
/// The comparison is case-sensitive and literal. Passwords are case-sensitive,
/// so a case-insensitive match would redact ordinary words that happen to differ
/// only in case from a secret, which is worse than the problem.
/// </remarks>
public sealed class SecretRedactor
{
    /// <summary>What replaces a secret, chosen to be obvious in a review.</summary>
    public const string Marker = "[redacted]";

    /// <summary>
    /// Configuration keys whose values are secrets.
    /// </summary>
    /// <remarks>
    /// Named individually. A rule like "anything containing Password" would grow
    /// silently wrong in both directions: it would miss a token and redact a
    /// setting that merely mentions the word.
    /// </remarks>
    private static readonly string[] SecretKeys =
    [
        "Erp:DatabasePassword",
        "Erp:AccessToken",
        "ConnectionStrings:Counter",
        "COUNTER_SQL_PASSWORD",
        "U2_PASSWORD",
        "U2_MCP_TOKEN",
    ];

    /// <summary>Shortest secret worth redacting.</summary>
    /// <remarks>
    /// A one- or two-character secret would match inside ordinary words and turn
    /// the whole trail into markers. Anything that short is not protecting
    /// anything, so refusing to redact it loses nothing.
    /// </remarks>
    private const int ShortestUsefulSecret = 4;

    private readonly string[] _secrets;

    /// <summary>
    /// Build a redactor from the values this process is configured with.
    /// </summary>
    /// <param name="configuration">Where the secrets are read from.</param>
    public SecretRedactor(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IEnumerable<string?> configured = SecretKeys
            .Select(key => configuration[key])
            .Concat(SecretKeys.Select(Environment.GetEnvironmentVariable));

        _secrets = configured
            .Where(value => value is { Length: >= ShortestUsefulSecret })
            .Select(value => value!)
            // A connection string is a secret, but it is not the secret anyone
            // pastes by accident: the password inside it is. Both are registered,
            // so a whole string and a bare password are each caught.
            .SelectMany(value => new[] { value }.Concat(PasswordsWithin(value)))
            .Where(value => value.Length >= ShortestUsefulSecret)
            .Distinct(StringComparer.Ordinal)
            // Longest first, so a secret that contains another is replaced whole
            // rather than leaving a fragment of it behind.
            .OrderByDescending(value => value.Length)
            .ToArray();
    }

    /// <summary>
    /// Pull the password out of anything shaped like a connection string.
    /// </summary>
    /// <remarks>
    /// Parsed by hand rather than with a connection-string builder, because this
    /// runs over values that may not be connection strings at all and a builder
    /// would throw on the ones that are not.
    /// </remarks>
    private static IEnumerable<string> PasswordsWithin(string value)
    {
        if (!value.Contains(';', StringComparison.Ordinal) ||
            !value.Contains('=', StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);

            if (pair.Length != 2)
            {
                continue;
            }

            string name = pair[0].Trim();

            bool isPassword =
                name.Equals("Password", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Pwd", StringComparison.OrdinalIgnoreCase);

            if (isPassword)
            {
                yield return pair[1].Trim();
            }
        }
    }

    /// <summary>Whether this process holds any secret worth redacting.</summary>
    public bool HasSecrets => _secrets.Length > 0;

    /// <summary>
    /// Return the text with every known secret replaced.
    /// </summary>
    /// <param name="text">What is about to be written down.</param>
    /// <returns>The same text, or one with markers where secrets were.</returns>
    [return: NotNullIfNotNull(nameof(text))]
    public string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text) || _secrets.Length == 0)
        {
            return text;
        }

        string result = text;

        foreach (string secret in _secrets)
        {
            if (result.Contains(secret, StringComparison.Ordinal))
            {
                result = result.Replace(secret, Marker, StringComparison.Ordinal);
            }
        }

        return result;
    }
}
