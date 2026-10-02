using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Boundary;
using Pragmatic.Actions.Configuration;
using Pragmatic.Actions.EFCore;
using Pragmatic.Actions.Extensions;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Actions.Pipeline.Filters;
using Pragmatic.Identity;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for AddPragmaticActions() and AddBoundary DI registration.
///     Covers: core service registration, options, filter registration, boundary CRUD.
/// </summary>
public class ServiceRegistrationTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class TestBoundary : IBoundary;
    private sealed class OtherBoundary : IBoundary;

    private sealed class StubUser : ICurrentUser
    {
        public string Id => "test";
        public string? DisplayName => "Test";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => new Dictionary<string, IReadOnlyList<string>>();
        public Pragmatic.Authorization.IUserAuthorization Authorization => Pragmatic.Authorization.FullAccessUserAuthorization.Instance;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    /// <summary>Helper: creates ServiceCollection with ICurrentUser registered (needed by PermissionAuthorizationFilter).</summary>
    private static ServiceCollection CreateServicesWithIdentity()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser, StubUser>();
        return services;
    }

    private sealed class TestAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    private sealed class CustomFilter : IActionFilter
    {
        public int Order => 500;

        public Task<VoidResult<IError>> BeforeExecuteAsync<TAction, TReturn>(TAction action, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => Task.FromResult(VoidResult<IError>.Success());

        public Task AfterExecuteAsync<TAction, TReturn>(TAction action, Result<TReturn, IError> result, CancellationToken ct)
            where TAction : DomainAction<TReturn>
            => Task.CompletedTask;
    }

    // =========================================================================
    // Tests — AddPragmaticActions basics
    // =========================================================================

    [Fact]
    public void AddPragmaticActions_RegistersLoggerFactory()
    {
        var services = new ServiceCollection();

        services.AddPragmaticActions();

        var provider = services.BuildServiceProvider();
        provider.GetService<ILoggerFactory>().Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticActions_RegistersGenericLogger()
    {
        var services = new ServiceCollection();

        services.AddPragmaticActions();

        var provider = services.BuildServiceProvider();
        provider.GetService<ILogger<ServiceRegistrationTests>>().Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticActions_ReturnsSameServiceCollectionForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddPragmaticActions();

        result.Should().BeSameAs(services);
    }

    // =========================================================================
    // Tests — Default filters (both enabled)
    // =========================================================================

    [Fact]
    public void AddPragmaticActions_DefaultOptions_RegistersValidationFilter()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().Contain(f => f is ValidationFilter);
    }

    [Fact]
    public void AddPragmaticActions_DefaultOptions_RegistersLoggingFilter()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().Contain(f => f is LoggingFilter);
    }

    // =========================================================================
    // Tests — Disable filters via options
    // =========================================================================

    [Fact]
    public void AddPragmaticActions_DisableValidationFilter_DoesNotRegisterIt()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions(opt => opt.EnableValidationFilter = false);
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().NotContain(f => f is ValidationFilter);
    }

    [Fact]
    public void AddPragmaticActions_DisableLoggingFilter_DoesNotRegisterIt()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions(opt => opt.EnableLoggingFilter = false);
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().NotContain(f => f is LoggingFilter);
    }

    // =========================================================================
    // Tests — AddActionFilter
    // =========================================================================

    [Fact]
    public void AddActionFilter_RegistersGlobalFilter()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions();
        services.AddActionFilter<CustomFilter>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().Contain(f => f is CustomFilter);
    }

    // =========================================================================
    // Tests — AddBoundary registration
    // =========================================================================

    [Fact]
    public void AddBoundary_RegistersBoundaryConfiguration()
    {
        var services = new ServiceCollection();

        services.AddBoundary<TestBoundary>(cfg => cfg
            .UseLocal()
            .UseDatabase(opt => opt.UseInMemoryDatabase("test")));

        var config = services.GetBoundaryConfiguration<TestBoundary>();
        config.Should().NotBeNull();
        config!.Mode.Should().Be(BoundaryMode.Local);
    }

    [Fact]
    public void AddBoundary_Remote_RegistersConfiguration()
    {
        var services = new ServiceCollection();

        services.AddBoundary<TestBoundary>(cfg => cfg
            .UseRemote("https://api.example.com"));

        var config = services.GetBoundaryConfiguration<TestBoundary>();
        config.Should().NotBeNull();
        config!.Mode.Should().Be(BoundaryMode.Remote);
        config.RemoteBaseUrl.Should().Be("https://api.example.com");
    }

    // =========================================================================
    // Tests — GetBoundaryConfiguration
    // =========================================================================

    [Fact]
    public void GetBoundaryConfiguration_NotRegistered_ReturnsNull()
    {
        var services = new ServiceCollection();

        var config = services.GetBoundaryConfiguration<TestBoundary>();

        config.Should().BeNull();
    }

    // =========================================================================
    // Tests — HasBoundary
    // =========================================================================

    [Fact]
    public void HasBoundary_Registered_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddBoundary<TestBoundary>(cfg => cfg
            .UseRemote("https://api.example.com"));

        services.HasBoundary<TestBoundary>().Should().BeTrue();
    }

    [Fact]
    public void HasBoundary_NotRegistered_ReturnsFalse()
    {
        var services = new ServiceCollection();

        services.HasBoundary<TestBoundary>().Should().BeFalse();
    }

    // =========================================================================
    // Tests — GetAllBoundaryConfigurations
    // =========================================================================

    [Fact]
    public void GetAllBoundaryConfigurations_ReturnsAll()
    {
        var services = new ServiceCollection();
        services.AddBoundary<TestBoundary>(cfg => cfg
            .UseRemote("https://test.example.com"));
        services.AddBoundary<OtherBoundary>(cfg => cfg
            .UseLocal()
            .UseDatabase(opt => opt.UseInMemoryDatabase("other")));

        var all = services.GetAllBoundaryConfigurations().ToList();

        all.Should().HaveCount(2);
        all.Should().Contain(c => c.BoundaryType == typeof(TestBoundary));
        all.Should().Contain(c => c.BoundaryType == typeof(OtherBoundary));
    }

    [Fact]
    public void GetAllBoundaryConfigurations_NoneRegistered_ReturnsEmpty()
    {
        var services = new ServiceCollection();

        var all = services.GetAllBoundaryConfigurations().ToList();

        all.Should().BeEmpty();
    }

    // =========================================================================
    // Tests — AddBoundary validates config
    // =========================================================================

    [Fact]
    public void AddBoundary_InvalidConfig_ThrowsDuringRegistration()
    {
        var services = new ServiceCollection();

        // UseLocal() without UseDatabase() should fail validation
        var act = () => services.AddBoundary<TestBoundary>(cfg => cfg.UseLocal());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*database configuration*");
    }

    // =========================================================================
    // Tests — PragmaticActionsOptions defaults
    // =========================================================================

    [Fact]
    public void PragmaticActionsOptions_DefaultValues()
    {
        var options = new PragmaticActionsOptions();

        options.EnableLoggingFilter.Should().BeTrue();
        options.EnableValidationFilter.Should().BeTrue();
        options.EnablePermissionFilter.Should().BeTrue();
        options.EnableResourceAuthorizationFilter.Should().BeTrue();
    }

    [Fact]
    public void AddPragmaticActions_DefaultOptions_RegistersPermissionFilter()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().Contain(f => f is PermissionAuthorizationFilter);
    }

    [Fact]
    public void AddPragmaticActions_DisablePermissionFilter_DoesNotRegisterIt()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions(opt => opt.EnablePermissionFilter = false);
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().NotContain(f => f is PermissionAuthorizationFilter);
    }

    [Fact]
    public void AddPragmaticActions_DefaultOptions_RegistersResourceAuthorizationFilter()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().Contain(f => f is ResourceAuthorizationFilter);
    }

    [Fact]
    public void AddPragmaticActions_DisableResourceAuthorizationFilter_DoesNotRegisterIt()
    {
        var services = CreateServicesWithIdentity();
        services.AddPragmaticActions(opt => opt.EnableResourceAuthorizationFilter = false);
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var filters = scope.ServiceProvider.GetServices<IActionFilter>().ToList();

        filters.Should().NotContain(f => f is ResourceAuthorizationFilter);
    }
}
