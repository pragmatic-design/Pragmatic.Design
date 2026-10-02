using Pragmatic.Authorization;

namespace Pragmatic.Identity;

/// <summary>
///     Represents the system context for background jobs, seed operations, and migrations.
///     Unlike <see cref="AnonymousUser" />, this is an authenticated context with full permissions.
/// </summary>
/// <remarks>
///     Use when code runs outside of a user request but still needs identity context,
///     e.g., scheduled jobs, data migrations, event handlers.
///     <code>
///     // In a background job
///     services.AddScoped&lt;ICurrentUser&gt;(_ => SystemUser.Instance);
///     </code>
/// </remarks>
public sealed class SystemUser : ICurrentUser
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyClaims =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    ///     Singleton instance. Use this instead of creating new instances.
    /// </summary>
    public static readonly SystemUser Instance = new();

    private SystemUser() { }

    /// <inheritdoc />
    public string Id => "system";

    /// <inheritdoc />
    public string? DisplayName => "System";

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public PrincipalKind Kind => PrincipalKind.System;

    /// <inheritdoc />
    /// <remarks>
    ///     Always <see langword="null"/>: the system context is tenant-agnostic and operates across all
    ///     tenants (jobs, migrations, seed). Tenant-scoped work must run under a tenant-bound principal,
    ///     not <see cref="SystemUser"/>.
    /// </remarks>
    public string? TenantId => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => EmptyClaims;

    /// <inheritdoc />

    /// <summary>System has full access by default.</summary>
    /// <inheritdoc />
    /// <remarks>
    ///     Declared rather than left to the interface default: this type is used directly, and a
    ///     default interface member is reachable only through the interface — <c>Instance.Delegation</c>
    ///     would not compile.
    /// </remarks>
    public IDelegationContext? Delegation => null;

    public IUserAuthorization Authorization => FullAccessUserAuthorization.Instance;

    /// <inheritdoc />
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
}
