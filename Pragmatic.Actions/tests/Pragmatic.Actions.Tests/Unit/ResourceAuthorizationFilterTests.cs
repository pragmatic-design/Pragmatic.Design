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
///     Tests for <see cref="ResourceAuthorizationFilter" />.
///     Covers: no authorizer, authorized, denied.
/// </summary>
public class ResourceAuthorizationFilterTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class UnprotectedAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    private sealed class ProtectedAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    private sealed class ProtectedVoidAction : VoidDomainAction
    {
        public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(VoidResult<IError>.Success());
    }

    private sealed class AllowAuthorizer : IResourceAuthorizer<ProtectedAction>
    {
        public ValueTask<bool> CanAccessAsync(
            ICurrentUser user, ProtectedAction resource, string action,
            CancellationToken ct = default)
            => ValueTask.FromResult(true);
    }

    private sealed class DenyAuthorizer : IResourceAuthorizer<ProtectedAction>
    {
        public ValueTask<bool> CanAccessAsync(
            ICurrentUser user, ProtectedAction resource, string action,
            CancellationToken ct = default)
            => ValueTask.FromResult(false);
    }

    private sealed class DenyVoidAuthorizer : IResourceAuthorizer<ProtectedVoidAction>
    {
        public ValueTask<bool> CanAccessAsync(
            ICurrentUser user, ProtectedVoidAction resource, string action,
            CancellationToken ct = default)
            => ValueTask.FromResult(false);
    }

    // A base action with an authorizer, and a derived action without one.
    private class BaseAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    private sealed class DerivedAction : BaseAction;

    private sealed class BaseAllowAuthorizer : IResourceAuthorizer<BaseAction>
    {
        public ValueTask<bool> CanAccessAsync(
            ICurrentUser user, BaseAction resource, string action,
            CancellationToken ct = default)
            => ValueTask.FromResult(true);
    }

    private sealed class FakeUser(bool authenticated) : ICurrentUser
    {
        public string Id => authenticated ? "user-1" : string.Empty;
        public string? DisplayName => authenticated ? "Test User" : null;
        public bool IsAuthenticated => authenticated;
        public PrincipalKind Kind => authenticated ? PrincipalKind.User : PrincipalKind.Anonymous;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => NullUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ResourceAuthorizationFilter CreateFilter(
        bool authenticated,
        Action<ServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new FakeUser(authenticated));
        configureServices?.Invoke(services);
        return new(services.BuildServiceProvider(),
            NullLogger<ResourceAuthorizationFilter>.Instance);
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public void Order_Is250()
    {
        var filter = CreateFilter(true);
        filter.Order.Should().Be(250);
    }

    [Fact]
    public async Task NoAuthorizer_AllowsExecution()
    {
        var filter = CreateFilter(true);
        var action = new UnprotectedAction();

        var result = await filter.BeforeExecuteAsync<UnprotectedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Authorizer_AllowsAccess_Succeeds()
    {
        var filter = CreateFilter(true, services =>
            services.AddSingleton<IResourceAuthorizer<ProtectedAction>, AllowAuthorizer>());
        var action = new ProtectedAction();

        var result = await filter.BeforeExecuteAsync<ProtectedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Authorizer_DeniesAccess_ReturnsForbidden()
    {
        var filter = CreateFilter(true, services =>
            services.AddSingleton<IResourceAuthorizer<ProtectedAction>, DenyAuthorizer>());
        var action = new ProtectedAction();

        var result = await filter.BeforeExecuteAsync<ProtectedAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task VoidAction_Authorizer_DeniesAccess_ReturnsForbidden()
    {
        var filter = CreateFilter(true, services =>
            services.AddSingleton<IResourceAuthorizer<ProtectedVoidAction>, DenyVoidAuthorizer>());
        var action = new ProtectedVoidAction();

        var result = await filter.BeforeExecuteVoidAsync<ProtectedVoidAction>(action, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task AfterExecuteAsync_IsNoOp()
    {
        var filter = CreateFilter(true);
        var action = new UnprotectedAction();

        await filter.AfterExecuteAsync<UnprotectedAction, string>(
            action, Result<string, IError>.Success("ok"), CancellationToken.None);
    }

    // =========================================================================
    // An authorizer registered for a base type
    // =========================================================================

    /// <summary>
    ///     A contravariant contract would say an authorizer for a base type applies to its derived
    ///     types. The DI container keys registrations by the closed generic and applies no variance, so
    ///     <c>GetService(IResourceAuthorizer&lt;Derived&gt;)</c> returns null and the action would sail
    ///     through. This pins the absence of the annotation, because adding <c>in</c> would make the
    ///     promise compile while nothing enforces it.
    /// </summary>
    [Fact]
    public void ResourceAuthorizer_IsNotContravariant()
        => typeof(IResourceAuthorizer<DerivedAction>)
            .IsAssignableFrom(typeof(IResourceAuthorizer<BaseAction>))
            .Should().BeFalse();

    [Fact]
    public async Task BaseTypeHasAuthorizer_DerivedAction_FailsClosed()
    {
        // The author registered an authorizer for BaseAction: they meant to protect this hierarchy.
        // DI will never hand that authorizer to DerivedAction, so passing silently is the one outcome
        // that must not happen.
        var filter = CreateFilter(true, services =>
        {
            services.AddSingleton<IResourceAuthorizer<BaseAction>, BaseAllowAuthorizer>();
            services.AddSingleton<IResourceAuthorizerCatalog>(
                new ResourceAuthorizerCatalog(typeof(BaseAction)));
        });

        var result = await filter.BeforeExecuteAsync<DerivedAction, string>(
            new DerivedAction(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task NoAuthorizerAnywhereInTheHierarchy_AllowsExecution()
    {
        // Resource authorization is opt-in: most actions have no authorizer by design, and a catalog
        // that covers unrelated types must not start denying them.
        var filter = CreateFilter(true, services =>
            services.AddSingleton<IResourceAuthorizerCatalog>(
                new ResourceAuthorizerCatalog(typeof(ProtectedAction))));

        var result = await filter.BeforeExecuteAsync<DerivedAction, string>(
            new DerivedAction(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ActionWithItsOwnAuthorizer_IsUnaffectedByTheCatalog()
    {
        var filter = CreateFilter(true, services =>
        {
            services.AddSingleton<IResourceAuthorizer<ProtectedAction>, AllowAuthorizer>();
            services.AddSingleton<IResourceAuthorizerCatalog>(
                new ResourceAuthorizerCatalog(typeof(ProtectedAction)));
        });

        var result = await filter.BeforeExecuteAsync<ProtectedAction, string>(
            new ProtectedAction(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
