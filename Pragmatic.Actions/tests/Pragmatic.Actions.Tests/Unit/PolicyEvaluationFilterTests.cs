using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for <see cref="PolicyEvaluationFilter" />.
///     Covers: no attribute, [RequirePolicy], unauthenticated, policy pass/fail, void actions.
/// </summary>
public class PolicyEvaluationFilterTests
{
    // =========================================================================
    // Test policies
    // =========================================================================

    public sealed class AdminOnlyPolicy : ResourcePolicy
    {
        public override bool Evaluate(ICurrentUser user)
            => user.Authorization.IsInRole("admin");
    }

    public sealed class AuthenticatedWithReadPolicy : ResourcePolicy
    {
        public override bool Evaluate(ICurrentUser user)
            => user.IsAuthenticated && user.Authorization.HasPermission("read");
    }

    // =========================================================================
    // Test doubles — Actions
    // =========================================================================

    private sealed class NoPolicyAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePolicy<AdminOnlyPolicy>]
    private sealed class AdminAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePolicy<AuthenticatedWithReadPolicy>]
    private sealed class ReadAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePolicy<AdminOnlyPolicy>]
    private sealed class VoidAdminAction : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());
    }

    // =========================================================================
    // Test doubles — ICurrentUser
    // =========================================================================

    private sealed class FakeUser(
        bool authenticated,
        HashSet<string> permissions,
        HashSet<string> roles) : ICurrentUser
    {
        public string Id => authenticated ? "user-1" : string.Empty;
        public string? DisplayName => authenticated ? "Test User" : null;
        public bool IsAuthenticated => authenticated;
        public PrincipalKind Kind => authenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => new FakeAuth(permissions, roles);
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private sealed class FakeAuth(HashSet<string> permissions, HashSet<string> roles) : IUserAuthorization
    {
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlySet<string> Permissions => permissions;
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public bool HasAnyPermission(IEnumerable<string> perms) => perms.Any(permissions.Contains);
        public bool HasAllPermissions(IEnumerable<string> perms) => perms.All(permissions.Contains);
        public bool IsInRole(string role) => roles.Contains(role);
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }

    // =========================================================================
    // Test doubles — IPolicyRegistry
    // =========================================================================

    private sealed class TestPolicyRegistry : IPolicyRegistry
    {
        private static readonly Dictionary<Type, Func<ResourcePolicy>> Factories = new()
        {
            [typeof(AdminAction)] = static () => new AdminOnlyPolicy(),
            [typeof(ReadAction)] = static () => new AuthenticatedWithReadPolicy(),
            [typeof(VoidAdminAction)] = static () => new AdminOnlyPolicy(),
        };

        public ResourcePolicy? GetPolicy(Type actionType)
            => Factories.TryGetValue(actionType, out var factory) ? factory() : null;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static PolicyEvaluationFilter CreateFilter(
        bool authenticated,
        HashSet<string>? permissions = null,
        HashSet<string>? roles = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeUser(authenticated, permissions ?? [], roles ?? []));
        return new(services.BuildServiceProvider(),
            NullLogger<PolicyEvaluationFilter>.Instance,
            new TestPolicyRegistry());
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public void Order_Is210()
    {
        var filter = CreateFilter(true);
        filter.Order.Should().Be(210);
    }

    [Fact]
    public async Task NoAttribute_AllowsExecution()
    {
        var filter = CreateFilter(false);
        var action = new NoPolicyAction();

        var result = await filter.BeforeExecuteAsync<NoPolicyAction, string>(action, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequirePolicy_UnauthenticatedUser_ReturnsUnauthorized()
    {
        var filter = CreateFilter(false);
        var action = new AdminAction();

        var result = await filter.BeforeExecuteAsync<AdminAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<UnauthorizedError>();
    }

    [Fact]
    public async Task RequirePolicy_UserSatisfiesPolicy_Succeeds()
    {
        var filter = CreateFilter(true, roles: ["admin"]);
        var action = new AdminAction();

        var result = await filter.BeforeExecuteAsync<AdminAction, string>(action, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequirePolicy_UserDoesNotSatisfyPolicy_ReturnsForbidden()
    {
        var filter = CreateFilter(true, roles: ["user"]);
        var action = new AdminAction();

        var result = await filter.BeforeExecuteAsync<AdminAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task RequirePolicy_CompositePolicy_BothConditionsMet_Succeeds()
    {
        var filter = CreateFilter(true, permissions: ["read"]);
        var action = new ReadAction();

        var result = await filter.BeforeExecuteAsync<ReadAction, string>(action, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequirePolicy_CompositePolicy_MissingPermission_ReturnsForbidden()
    {
        var filter = CreateFilter(true, permissions: ["write"]);
        var action = new ReadAction();

        var result = await filter.BeforeExecuteAsync<ReadAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task VoidAction_PolicySatisfied_Succeeds()
    {
        var filter = CreateFilter(true, roles: ["admin"]);
        var action = new VoidAdminAction();

        var result = await filter.BeforeExecuteVoidAsync<VoidAdminAction>(action, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task VoidAction_PolicyNotSatisfied_ReturnsForbidden()
    {
        var filter = CreateFilter(true, roles: ["user"]);
        var action = new VoidAdminAction();

        var result = await filter.BeforeExecuteVoidAsync<VoidAdminAction>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task AfterExecuteAsync_IsNoOp()
    {
        var filter = CreateFilter(true);
        var action = new NoPolicyAction();

        await filter.AfterExecuteAsync<NoPolicyAction, string>(
            action, Result<string, IError>.Success("ok"), CancellationToken.None);
    }

    [Fact]
    public async Task AfterExecuteVoidAsync_IsNoOp()
    {
        var filter = CreateFilter(true);
        var action = new VoidAdminAction();

        await filter.AfterExecuteVoidAsync<VoidAdminAction>(
            action, VoidResult<IError>.Success(), CancellationToken.None);
    }
}
