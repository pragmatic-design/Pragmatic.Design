using Pragmatic.Identity;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Minimal <see cref="ICurrentUser"/> implementation for the authorization samples, so policies
///     and providers can be evaluated against a realistic user without an HTTP host.
/// </summary>
internal sealed class SampleUser(
    string id,
    string? displayName = null,
    bool isAuthenticated = true,
    PrincipalKind kind = PrincipalKind.User,
    string? tenantId = null,
    IReadOnlyList<string>? roles = null,
    IReadOnlyList<string>? permissions = null,
    IReadOnlyList<string>? groups = null,
    IReadOnlyList<string>? scopes = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? claims = null)
    : ICurrentUser
{
    public string Id { get; } = id;
    public string? DisplayName { get; } = displayName;
    public bool IsAuthenticated { get; } = isAuthenticated;
    public PrincipalKind Kind { get; } = kind;
    public string? TenantId { get; } = tenantId;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } = claims ?? new Dictionary<string, IReadOnlyList<string>>();
    public IUserAuthorization Authorization { get; } = new SampleUserAuthorization(
        roles ?? [],
        permissions ?? [],
        groups ?? [],
        scopes ?? []);

    public IAuthenticationContext Authentication { get; } = NullAuthenticationContext.Instance;
}
