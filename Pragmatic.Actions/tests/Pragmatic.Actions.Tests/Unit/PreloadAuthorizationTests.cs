using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     <see cref="PreloadAuthorization.RequireAllAsync" />: the read permission a preload asks, decided as the
///     operation's own permission is.
/// </summary>
/// <remarks><c>RequireReadPermission = true</c> on <c>[LoadEntity]</c> / <c>[LoadEntities]</c>.</remarks>
public class PreloadAuthorizationTests
{
    private const string Read = "people.employee.read";

    private sealed class FakeUser(bool authenticated, HashSet<string> permissions) : ICurrentUser
    {
        public string Id => authenticated ? "user-1" : string.Empty;
        public string? DisplayName => authenticated ? "Test User" : null;
        public bool IsAuthenticated => authenticated;
        public PrincipalKind Kind => authenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => new FakeAuthorization(permissions);
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private sealed class FakeAuthorization(HashSet<string> permissions) : IUserAuthorization
    {
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions => permissions;
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public bool HasAnyPermission(IEnumerable<string> perms) => perms.Any(permissions.Contains);
        public bool HasAllPermissions(IEnumerable<string> perms) => perms.All(permissions.Contains);
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }

    private static IServiceProvider Services(bool authenticated, HashSet<string> permissions, ActionCallContext? callContext = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeUser(authenticated, permissions));
        if (callContext is not null)
            services.AddSingleton<global::Pragmatic.Pipeline.ICallContext>(callContext);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ACallerWithThePermission_IsLetThrough()
    {
        var refusal = await PreloadAuthorization.RequireAllAsync(
            Services(authenticated: true, [Read]), [Read], typeof(PreloadAuthorizationTests), CancellationToken.None);

        refusal.Should().BeNull();
    }

    [Fact]
    public async Task ACallerWithoutIt_IsForbidden()
    {
        var refusal = await PreloadAuthorization.RequireAllAsync(
            Services(authenticated: true, ["people.team.read"]), [Read], typeof(PreloadAuthorizationTests), CancellationToken.None);

        refusal.Should().NotBeNull();
        refusal!.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task NobodySignedIn_IsUnauthorized()
    {
        var refusal = await PreloadAuthorization.RequireAllAsync(
            Services(authenticated: false, []), [Read], typeof(PreloadAuthorizationTests), CancellationToken.None);

        refusal.Should().NotBeNull();
        refusal!.StatusCode.Should().Be(401);
    }

    /// <summary>An internal call is not asked, as it is not asked the operation's own permission.</summary>
    [Fact]
    public async Task AnInternalCall_IsNotAsked()
    {
        var callContext = new ActionCallContext();
        using var _ = callContext.EnterInternalCall();

        var refusal = await PreloadAuthorization.RequireAllAsync(
            Services(authenticated: false, [], callContext), [Read], typeof(PreloadAuthorizationTests), CancellationToken.None);

        refusal.Should().BeNull();
    }
}
