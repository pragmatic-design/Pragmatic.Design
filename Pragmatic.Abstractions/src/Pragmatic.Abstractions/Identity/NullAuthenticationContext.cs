namespace Pragmatic.Identity;

/// <summary>
///     No-op <see cref="IAuthenticationContext"/> for anonymous, system, or non-HTTP contexts.
///     All properties return <c>null</c> / <c>false</c>.
/// </summary>
public sealed class NullAuthenticationContext : IAuthenticationContext
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullAuthenticationContext Instance = new();

    private NullAuthenticationContext() { }

    /// <inheritdoc />
    public string? Scheme => null;

    /// <inheritdoc />
    public string? Protocol => null;

    /// <inheritdoc />
    public string? Issuer => null;

    /// <inheritdoc />
    public string? Subject => null;

    /// <inheritdoc />
    public bool IsMfaAuthenticated => false;

    /// <inheritdoc />
    public DateTimeOffset? AuthenticatedAt => null;

    /// <inheritdoc />
    public DateTimeOffset? ExpiresAt => null;

    /// <inheritdoc />
    public string? ExternalIdentityKey => null;
}
