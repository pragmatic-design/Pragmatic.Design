namespace Pragmatic.Identity.Local.Services;

/// <summary>
///     What the token of a sign-in will say about the caller — built by the sign-in, completed by the
///     application's <see cref="IUserClaimsContributor" />s, and signed by the <see cref="IAccessTokenIssuer" />.
/// </summary>
/// <remarks>
///     <para>
///         The account's key and its security stamp come from the identity and cannot be changed: the key
///         is how every request finds the account again, and the stamp is what revokes the token when the
///         password changes. A token without it is refused on first use (<c>RequireSecurityStamp</c>), so
///         it is not left to each application to remember.
///     </para>
///     <para>
///         The subject starts as the account's key. An application that records who acted by a reference
///         of its own — a pseudonym from the subject registry, rather than an email — sets it here.
///     </para>
/// </remarks>
public sealed class SignInClaims
{
    /// <summary>The claims of a sign-in to <paramref name="identity" />.</summary>
    public SignInClaims(LocalIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ExternalIdentityKey = identity.ExternalIdentityKey;
        SecurityStamp = identity.SecurityStamp;
        Subject = identity.ExternalIdentityKey;
    }

    /// <summary>The account's key: how a request carrying the token finds the account again.</summary>
    public string ExternalIdentityKey { get; }

    /// <summary>The account's security stamp when the token is signed.</summary>
    public string SecurityStamp { get; }

    /// <summary>Who the token names as the one acting. The account's key unless the application sets another.</summary>
    public string Subject { get; set; }

    /// <summary>The name shown for the caller.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The tenant the caller acts in.</summary>
    public string? TenantId { get; set; }

    /// <summary>The roles the caller holds; the host maps each to its permissions.</summary>
    public IList<string> Roles { get; } = [];

    /// <summary>Permissions granted directly, beyond those of the roles.</summary>
    public IList<string> Permissions { get; } = [];
}
