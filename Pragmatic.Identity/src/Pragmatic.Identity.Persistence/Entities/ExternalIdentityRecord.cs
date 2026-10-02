using Pragmatic.Persistence.Entity;

namespace Pragmatic.Identity.Persistence.Entities;

/// <summary>
///     Links a local user to an external identity provider (e.g., Auth0, Entra ID, Keycloak).
///     Multiple providers can be linked to the same user.
/// </summary>
/// <typeparam name="TKey">The type of the user's primary key.</typeparam>
public class ExternalIdentityRecord<TKey> : IAuditable
    where TKey : notnull
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the local user.</summary>
    public TKey UserId { get; set; } = default!;

    /// <summary>Identity provider name (e.g., "Auth0", "EntraID", "Keycloak").</summary>
    public required string Provider { get; set; }

    /// <summary>Issuer URI from the identity token.</summary>
    public required string Issuer { get; set; }

    /// <summary>Subject claim value — unique user identifier at the provider.</summary>
    public required string Subject { get; set; }

    /// <summary>
    ///     Canonical external identity key. Indexed for lookups by external identity.
    /// </summary>
    /// <remarks>
    ///     Through the shared composer, so every producer of the key applies the same escaping: a
    ///     key built by hand would disagree with the others for any issuer or
    ///     subject containing the separator.
    /// </remarks>
    public string ExternalIdentityKey
        => Pragmatic.Identity.ExternalIdentityKey.Compose(Issuer, Subject) ?? string.Empty;

    /// <summary>Last time this identity was used to authenticate.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    // IAuditable
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
