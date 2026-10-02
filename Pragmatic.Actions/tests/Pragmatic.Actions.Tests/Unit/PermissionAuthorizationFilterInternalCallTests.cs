using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for the internal-call bypass in <see cref="PermissionAuthorizationFilter" />.
///     When <see cref="ActionCallContext.IsInternalCall" /> is true, permission checks are skipped
///     even for unauthenticated users and missing permissions (intra-boundary / system reactions).
/// </summary>
public class PermissionAuthorizationFilterInternalCallTests
{
    // Distinct action types so the static requirement cache is not shared with other test classes.
    [RequirePermission("internal.secure")]
    private sealed class InternalGuardedAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePermission("internal.secure.void")]
    private sealed class InternalGuardedVoidAction : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());
    }

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

    private sealed class TestPermissionRegistry : IPermissionRequirementRegistry
    {
        private static readonly Dictionary<Type, PermissionRequirementEntry> Entries = new()
        {
            [typeof(InternalGuardedAction)] = new(["internal.secure"], RequireAll: true),
            [typeof(InternalGuardedVoidAction)] = new(["internal.secure.void"], RequireAll: true),
        };

        public PermissionRequirementEntry? GetRequirement(Type actionType)
            => Entries.TryGetValue(actionType, out var entry) ? entry : null;
    }

    private static PermissionAuthorizationFilter CreateFilter(
        bool authenticated, HashSet<string> permissions, ActionCallContext? callContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeUser(authenticated, permissions));
        if (callContext is not null)
        {
            // Both, as AddPragmaticActions registers them. The readers resolve ICallContext; a
            // container that registers only the concrete type would let reader and test agree on a
            // type nothing else uses, and hide a reader that resolves the wrong one.
            services.AddSingleton(callContext);
            services.AddSingleton<global::Pragmatic.Pipeline.ICallContext>(callContext);
        }
        return new(services.BuildServiceProvider(),
            NullLogger<PermissionAuthorizationFilter>.Instance,
            new TestPermissionRegistry());
    }

    [Fact]
    public async Task BeforeExecuteAsync_InternalCall_BypassesPermissionForUnauthenticatedUser()
    {
        var callContext = new ActionCallContext();
        using var _ = callContext.EnterInternalCall();
        var filter = CreateFilter(authenticated: false, permissions: [], callContext);
        var action = new InternalGuardedAction();

        var result = await filter.BeforeExecuteAsync<InternalGuardedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BeforeExecuteAsync_InternalCall_BypassesPermissionWhenPermissionMissing()
    {
        var callContext = new ActionCallContext();
        using var _ = callContext.EnterInternalCall();
        var filter = CreateFilter(authenticated: true, permissions: [], callContext);
        var action = new InternalGuardedAction();

        var result = await filter.BeforeExecuteAsync<InternalGuardedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BeforeExecuteAsync_NotInternalCall_EnforcesPermission()
    {
        // Scope NOT entered: IsInternalCall is false → normal enforcement applies.
        var callContext = new ActionCallContext();
        var filter = CreateFilter(authenticated: true, permissions: [], callContext);
        var action = new InternalGuardedAction();

        var result = await filter.BeforeExecuteAsync<InternalGuardedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task BeforeExecuteAsync_InternalCallScopeExited_EnforcesPermissionAgain()
    {
        var callContext = new ActionCallContext();
        var filter = CreateFilter(authenticated: false, permissions: [], callContext);
        var action = new InternalGuardedAction();

        using (callContext.EnterInternalCall())
        {
            var bypassed = await filter.BeforeExecuteAsync<InternalGuardedAction, string>(action, CancellationToken.None);
            bypassed.IsSuccess.Should().BeTrue();
        }

        callContext.IsInternalCall.Should().BeFalse();
        var enforced = await filter.BeforeExecuteAsync<InternalGuardedAction, string>(action, CancellationToken.None);
        enforced.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task BeforeExecuteVoidAsync_InternalCall_BypassesPermission()
    {
        var callContext = new ActionCallContext();
        using var _ = callContext.EnterInternalCall();
        var filter = CreateFilter(authenticated: false, permissions: [], callContext);
        var action = new InternalGuardedVoidAction();

        var result = await filter.BeforeExecuteVoidAsync<InternalGuardedVoidAction>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
