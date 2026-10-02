using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Endpoints.Extensions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPragmaticEndpoints_RegistersOptions()
    {
        var services = new ServiceCollection();

        services.AddPragmaticEndpoints();

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<PragmaticEndpointsOptions>();
        options.Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticEndpoints_NullConfigure_RegistersDefaults()
    {
        var services = new ServiceCollection();

        services.AddPragmaticEndpoints();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PragmaticEndpointsOptions>();
        options.RoutePrefix.Should().BeEmpty();
        options.EnableOpenApi.Should().BeTrue();
    }

    [Fact]
    public void AddPragmaticEndpoints_WithConfigure_AppliesConfiguration()
    {
        var services = new ServiceCollection();

        services.AddPragmaticEndpoints(o => o.RoutePrefix = "/api/v1");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PragmaticEndpointsOptions>();
        options.RoutePrefix.Should().Be("/api/v1");
    }

    [Fact]
    public void AddPragmaticEndpoints_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddPragmaticEndpoints();

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void AddPragmaticEndpoints_OptionsAreSingleton()
    {
        var services = new ServiceCollection();
        services.AddPragmaticEndpoints();

        var descriptor = services.Should().Contain(d => d.ServiceType == typeof(PragmaticEndpointsOptions)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddPragmaticEndpoints_CustomRoutePrefix_Applied()
    {
        var services = new ServiceCollection();

        services.AddPragmaticEndpoints(o => o.RoutePrefix = "/custom");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PragmaticEndpointsOptions>();
        options.RoutePrefix.Should().Be("/custom");
    }

    [Fact]
    public void AddPragmaticEndpoints_ConfigureGroup_Applied()
    {
        var services = new ServiceCollection();

        services.AddPragmaticEndpoints(o =>
            o.ConfigureGroup("Orders", g => g.RoutePrefix = "/orders"));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PragmaticEndpointsOptions>();
        options.Groups.Should().ContainKey("Orders");
        options.Groups["Orders"].RoutePrefix.Should().Be("/orders");
    }

    [Fact]
    public void AddPragmaticEndpoints_MultipleGroups_AllConfigured()
    {
        var services = new ServiceCollection();

        services.AddPragmaticEndpoints(o =>
        {
            o.ConfigureGroup("Orders", g => g.RoutePrefix = "/orders");
            o.ConfigureGroup("Users", g => g.RoutePrefix = "/users");
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PragmaticEndpointsOptions>();
        options.Groups.Should().HaveCount(2);
        options.Groups["Orders"].RoutePrefix.Should().Be("/orders");
        options.Groups["Users"].RoutePrefix.Should().Be("/users");
    }
}
