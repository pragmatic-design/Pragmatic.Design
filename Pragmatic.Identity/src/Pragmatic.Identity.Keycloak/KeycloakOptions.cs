namespace Pragmatic.Identity.Keycloak;

/// <summary>
///     Options for Keycloak authentication and admin access. The authority is derived as
///     <c>{BaseUrl}/realms/{Realm}</c>; the admin client uses <see cref="ClientId"/>/<see cref="ClientSecret"/>
///     (client-credentials) against the realm's admin REST API.
/// </summary>
public sealed class KeycloakOptions
{
    /// <summary>Keycloak base URL, e.g. <c>https://keycloak.example.com</c>.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The realm name.</summary>
    public string Realm { get; set; } = "";

    /// <summary>The expected token audience. When null, audience validation is disabled.</summary>
    public string? Audience { get; set; }

    /// <summary>Confidential client id used for admin/provisioning calls.</summary>
    public string? ClientId { get; set; }

    /// <summary>Confidential client secret used for admin/provisioning calls.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Whether HTTPS metadata is required. Keep <c>true</c> outside local development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    ///     Optional allow-list of realm role names. When set (non-null and non-empty), only roles whose name
    ///     is in this set are mapped into the internal role claim; all others are dropped. Combined with
    ///     <see cref="RolePrefix" /> a role must satisfy BOTH. Default <see langword="null" /> = every realm
    ///     role passes (unchanged behaviour). Use this to constrain roles from a shared Keycloak realm.
    /// </summary>
    public IReadOnlyCollection<string>? AllowedRoles { get; set; }

    /// <summary>
    ///     Optional case-sensitive prefix a realm role name must start with to be mapped (e.g. <c>"myapp:"</c>).
    ///     Default <see langword="null" /> = no prefix filter. Combined with <see cref="AllowedRoles" /> a
    ///     role must satisfy BOTH filters.
    /// </summary>
    public string? RolePrefix { get; set; }

    /// <summary>The OIDC authority for this realm.</summary>
    public string Authority => $"{BaseUrl.TrimEnd('/')}/realms/{Realm}";
}
