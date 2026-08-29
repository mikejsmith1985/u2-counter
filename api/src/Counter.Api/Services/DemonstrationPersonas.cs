namespace Counter.Api.Services;

/// <summary>
/// The people this demonstration can be signed in as.
/// </summary>
/// <remarks>
/// The specification assumes users arrive already identified; establishing that
/// identity is not part of this feature. What the feature needs is a real
/// identity flowing through to the audit trail, and personas provide one without
/// adding an identity provider, a secret, and a failure mode that proves nothing
/// about counter work.
///
/// The MCP server behind this already bridges to Duo, Auth0 or any OIDC
/// provider, so the path to real single sign-on exists and is documented. It is
/// simply not exercised here, and the sign-in screen says so.
/// </remarks>
public static class DemonstrationPersonas
{
    /// <summary>Everyone this demonstration can sign in as.</summary>
    public static readonly IReadOnlyList<Persona> All =
    [
        new("demo|dana", "Dana Whitfield", "DEN", IsReadOnly: true,
            "Everyday counter work at the Denver branch"),
        new("demo|marcus", "Marcus Oyelaran", "BOU", IsReadOnly: true,
            "Boulder, a branch that stocks little — good for seeing transfers"),
        new("demo|priya", "Priya Raghavan", "DEN", IsReadOnly: true,
            "Denver, for reviewing what the system recorded"),
    ];

    /// <summary>Find a persona by its subject.</summary>
    /// <param name="subject">The identity, as the sign-in returns it.</param>
    public static Persona? Find(string subject) =>
        All.FirstOrDefault(persona => persona.Subject == subject);
}

/// <summary>
/// A demonstration identity.
/// </summary>
/// <param name="Subject">Stable identifier, as an identity provider would give.</param>
/// <param name="DisplayName">How they are named on screen and in the record.</param>
/// <param name="HomeBranchCode">Which branch's figures lead on the grid.</param>
/// <param name="IsReadOnly">
/// True for every persona. This release answers questions; it changes nothing,
/// and no control that would change data is offered to any of them.
/// </param>
/// <param name="Description">What this persona is useful for showing.</param>
public sealed record Persona(
    string Subject,
    string DisplayName,
    string HomeBranchCode,
    bool IsReadOnly,
    string Description);
