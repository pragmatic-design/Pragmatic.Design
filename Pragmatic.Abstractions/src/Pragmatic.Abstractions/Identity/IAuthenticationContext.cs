namespace Pragmatic.Identity;

/// <summary>
///     Provides authentication metadata for the current user.
///     Accessed via <c>ICurrentUser.Authentication</c> — captures how the user was authenticated.
/// </summary>
public interface IAuthenticationContext
{
    /// <summary>Authentication scheme used (e.g., "Bearer", "Cookie").</summary>
    string? Scheme { get; }

    /// <summary>Protocol (e.g., "oidc", "saml2", "apikey").</summary>
    string? Protocol { get; }

    /// <summary>Token issuer (e.g., "https://login.example.com").</summary>
    string? Issuer { get; }

    /// <summary>Subject identifier from the identity provider.</summary>
    string? Subject { get; }

    /// <summary>Whether the user completed multi-factor authentication.</summary>
    bool IsMfaAuthenticated { get; }

    /// <summary>When the authentication occurred.</summary>
    DateTimeOffset? AuthenticatedAt { get; }

    /// <summary>When the current credential expires.</summary>
    DateTimeOffset? ExpiresAt { get; }

    /// <summary>External identity key in "{issuer}|{subject}" format for IdP correlation.</summary>
    string? ExternalIdentityKey { get; }
}
