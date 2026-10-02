using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for <see cref="PermissionAuthorizationFilter" />.
///     Covers: no attribute, [RequirePermission], [RequireAnyPermission],
///     unauthenticated user, missing permissions.
/// </summary>
public class PermissionAuthorizationFilterTests
{
    // =========================================================================
    // Test doubles — Actions
    // =========================================================================

    private sealed class NoPermissionAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePermission("orders.create")]
    private sealed class SinglePermissionAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePermission("orders.create", "orders.approve")]
    private sealed class MultiplePermissionsAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequireAnyPermission("orders.create", "orders.admin")]
    private sealed class AnyPermissionAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePermission("admin.manage")]
    private sealed class VoidPermissionAction : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());
    }

    // =========================================================================
    // Test doubles — ICurrentUser
    // =========================================================================

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

    // =========================================================================
    // Test doubles — IPermissionRequirementRegistry
    // =========================================================================

    private sealed class TestPermissionRegistry : IPermissionRequirementRegistry
    {
        private static readonly Dictionary<Type, PermissionRequirementEntry> Entries = new()
        {
            [typeof(SinglePermissionAction)] = new(["orders.create"], RequireAll: true),
            [typeof(MultiplePermissionsAction)] = new(["orders.create", "orders.approve"], RequireAll: true),
            [typeof(AnyPermissionAction)] = new(["orders.create", "orders.admin"], RequireAll: false),
            [typeof(VoidPermissionAction)] = new(["admin.manage"], RequireAll: true),
        };

        public PermissionRequirementEntry? GetRequirement(Type actionType)
            => Entries.TryGetValue(actionType, out var entry) ? entry : null;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static PermissionAuthorizationFilter CreateFilter(bool authenticated, HashSet<string> permissions)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeUser(authenticated, permissions));
        return new(services.BuildServiceProvider(),
            NullLogger<PermissionAuthorizationFilter>.Instance,
            new TestPermissionRegistry());
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public void Order_Is200()
    {
        var filter = CreateFilter(true, []);
        filter.Order.Should().Be(200);
    }

    [Fact]
    public async Task NoAttribute_AllowsExecution()
    {
        var filter = CreateFilter(false, []);
        var action = new NoPermissionAction();

        var result = await filter.BeforeExecuteAsync<NoPermissionAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequirePermission_UnauthenticatedUser_ReturnsUnauthorized()
    {
        var filter = CreateFilter(false, []);
        var action = new SinglePermissionAction();

        var result = await filter.BeforeExecuteAsync<SinglePermissionAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<UnauthorizedError>();
    }

    [Fact]
    public async Task RequirePermission_UserHasPermission_Succeeds()
    {
        var filter = CreateFilter(true, ["orders.create"]);
        var action = new SinglePermissionAction();

        var result = await filter.BeforeExecuteAsync<SinglePermissionAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequirePermission_UserMissingPermission_ReturnsForbidden()
    {
        var filter = CreateFilter(true, ["orders.read"]);
        var action = new SinglePermissionAction();

        var result = await filter.BeforeExecuteAsync<SinglePermissionAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task RequirePermission_Multiple_UserHasAll_Succeeds()
    {
        var filter = CreateFilter(true, ["orders.create", "orders.approve"]);
        var action = new MultiplePermissionsAction();

        var result = await filter.BeforeExecuteAsync<MultiplePermissionsAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequirePermission_Multiple_UserMissingOne_ReturnsForbidden()
    {
        var filter = CreateFilter(true, ["orders.create"]);
        var action = new MultiplePermissionsAction();

        var result = await filter.BeforeExecuteAsync<MultiplePermissionsAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task RequireAnyPermission_UserHasOne_Succeeds()
    {
        var filter = CreateFilter(true, ["orders.admin"]);
        var action = new AnyPermissionAction();

        var result = await filter.BeforeExecuteAsync<AnyPermissionAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequireAnyPermission_UserHasNone_ReturnsForbidden()
    {
        var filter = CreateFilter(true, ["orders.read"]);
        var action = new AnyPermissionAction();

        var result = await filter.BeforeExecuteAsync<AnyPermissionAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task VoidAction_RequirePermission_UnauthenticatedUser_ReturnsUnauthorized()
    {
        var filter = CreateFilter(false, []);
        var action = new VoidPermissionAction();

        var result = await filter.BeforeExecuteVoidAsync<VoidPermissionAction>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<UnauthorizedError>();
    }

    [Fact]
    public async Task VoidAction_RequirePermission_UserHasPermission_Succeeds()
    {
        var filter = CreateFilter(true, ["admin.manage"]);
        var action = new VoidPermissionAction();

        var result = await filter.BeforeExecuteVoidAsync<VoidPermissionAction>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AfterExecuteAsync_IsNoOp()
    {
        var filter = CreateFilter(true, []);
        var action = new NoPermissionAction();

        // Should complete without error
        await filter.AfterExecuteAsync<NoPermissionAction, string>(
            action, Result<string, IError>.Success("ok"), CancellationToken.None);
    }

    [Fact]
    public async Task AfterExecuteVoidAsync_IsNoOp()
    {
        var filter = CreateFilter(true, []);
        var action = new VoidPermissionAction();

        // Should complete without error
        await filter.AfterExecuteVoidAsync<VoidPermissionAction>(
            action, VoidResult<IError>.Success(), CancellationToken.None);
    }
}
