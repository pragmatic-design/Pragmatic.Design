namespace Pragmatic.Identity.Oidc;

/// <summary>
///     Options for generic OIDC bearer authentication: which external identity provider issues the tokens and
///     how its role claim maps into the Pragmatic authorization pipeline. Provider-specific packages (e.g.
///     Keycloak) layer their conventions on top.
/// </summary>
public sealed class OidcOptions
{
    /// <summary>The OIDC authority (issuer) URL — its discovery document supplies the signing keys.</summary>
    public string Authority { get; set; } = "";

    /// <summary>The expected audience. When null, audience validation is disabled.</summary>
    public string? Audience { get; set; }

    /// <summary>The token claim carrying roles. A single role, or a JSON array of roles, are both accepted.</summary>
    public string RoleClaim { get; set; } = "roles";

    /// <summary>
    ///     The token claim carrying the display name: what <c>User.Identity.Name</c> and
    ///     <c>ICurrentUser.DisplayName</c> both read.
    /// </summary>
    public string NameClaim { get; set; } = "name";

    /// <summary>Whether HTTPS metadata is required. Keep <c>true</c> outside local development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    ///     Optional allow-list of role names. When set (non-null and non-empty), only roles whose name is in
    ///     this set are mapped into the internal role claim; all others are dropped. Combined with
    ///     <see cref="RolePrefix" /> a role must satisfy BOTH. Default <see langword="null" /> = every role
    ///     passes (unchanged behaviour). Use this to constrain roles from a shared/mis-scoped external IdP.
    /// </summary>
    public IReadOnlyCollection<string>? AllowedRoles { get; set; }

    /// <summary>
    ///     Optional case-sensitive prefix a role name must start with to be mapped (e.g. <c>"myapp:"</c>).
    ///     Default <see langword="null" /> = no prefix filter. Combined with <see cref="AllowedRoles" /> a
    ///     role must satisfy BOTH filters.
    /// </summary>
    public string? RolePrefix { get; set; }
}
