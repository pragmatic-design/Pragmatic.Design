using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Policy;
using Pragmatic.Identity;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for the internal-call bypass in <see cref="PolicyEvaluationFilter" />.
///     When <see cref="ActionCallContext.IsInternalCall" /> is true, policy evaluation is skipped
///     entirely (the policy is never invoked), regardless of authentication or policy result.
/// </summary>
public class PolicyEvaluationFilterInternalCallTests
{
    private sealed class DenyAllPolicy : ResourcePolicy
    {
        public override bool Evaluate(ICurrentUser user) => false;
    }

    [RequirePolicy<DenyAllPolicy>]
    private sealed class InternalDeniedAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    [RequirePolicy<DenyAllPolicy>]
    private sealed class InternalDeniedVoidAction : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());
    }

    private sealed class FakeUser(bool authenticated) : ICurrentUser
    {
        public string Id => authenticated ? "user-1" : string.Empty;
        public string? DisplayName => authenticated ? "Test User" : null;
        public bool IsAuthenticated => authenticated;
        public PrincipalKind Kind => authenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => new FakeAuth();
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private sealed class FakeAuth : IUserAuthorization
    {
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => false;
        public bool HasAnyPermission(IEnumerable<string> perms) => false;
        public bool HasAllPermissions(IEnumerable<string> perms) => false;
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }

    private sealed class TestPolicyRegistry : IPolicyRegistry
    {
        private static readonly Dictionary<Type, Func<ResourcePolicy>> Factories = new()
        {
            [typeof(InternalDeniedAction)] = static () => new DenyAllPolicy(),
            [typeof(InternalDeniedVoidAction)] = static () => new DenyAllPolicy(),
        };

        public ResourcePolicy? GetPolicy(Type actionType)
            => Factories.TryGetValue(actionType, out var factory) ? factory() : null;
    }

    private static PolicyEvaluationFilter CreateFilter(bool authenticated, ActionCallContext? callContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeUser(authenticated));
        if (callContext is not null)
        {
            // Both, as AddPragmaticActions registers them. The readers resolve ICallContext; a
            // container that registers only the concrete type would let reader and test agree on a
            // type nothing else uses, and hide a reader that resolves the wrong one.
            services.AddSingleton(callContext);
            services.AddSingleton<global::Pragmatic.Pipeline.ICallContext>(callContext);
        }
        return new(services.BuildServiceProvider(),
            NullLogger<PolicyEvaluationFilter>.Instance,
            new TestPolicyRegistry());
    }

    [Fact]
    public async Task BeforeExecuteAsync_InternalCall_SkipsDenyAllPolicy()
    {
        var callContext = new ActionCallContext();
        using var _ = callContext.EnterInternalCall();
        var filter = CreateFilter(authenticated: true, callContext);
        var action = new InternalDeniedAction();

        var result = await filter.BeforeExecuteAsync<InternalDeniedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BeforeExecuteAsync_InternalCall_SkipsPolicyForUnauthenticatedUser()
    {
        var callContext = new ActionCallContext();
        using var _ = callContext.EnterInternalCall();
        var filter = CreateFilter(authenticated: false, callContext);
        var action = new InternalDeniedAction();

        var result = await filter.BeforeExecuteAsync<InternalDeniedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BeforeExecuteAsync_NotInternalCall_EnforcesDenyAllPolicy()
    {
        var callContext = new ActionCallContext();
        var filter = CreateFilter(authenticated: true, callContext);
        var action = new InternalDeniedAction();

        var result = await filter.BeforeExecuteAsync<InternalDeniedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task BeforeExecuteVoidAsync_InternalCall_SkipsDenyAllPolicy()
    {
        var callContext = new ActionCallContext();
        using var _ = callContext.EnterInternalCall();
        var filter = CreateFilter(authenticated: true, callContext);
        var action = new InternalDeniedVoidAction();

        var result = await filter.BeforeExecuteVoidAsync<InternalDeniedVoidAction>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
