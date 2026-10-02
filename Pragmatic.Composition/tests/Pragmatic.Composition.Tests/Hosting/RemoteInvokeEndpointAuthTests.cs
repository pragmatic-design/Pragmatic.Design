using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Remote;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     The generated <c>/_pragmatic/invoke</c> endpoint must be fail-closed by default and
///     opt out of authorization only when explicitly configured.
/// </summary>
public class RemoteInvokeEndpointAuthTests
{
    [Fact]
    public void Decide_WithDefaults_RequiresAuthenticated()
    {
        var options = new PragmaticRemoteInvokeOptions();

        RemoteInvokeEndpointAuth.Decide(options)
            .Should().Be(RemoteInvokeEndpointAuth.Mode.RequireAuthenticated);
    }

    [Fact]
    public void Decide_WithPolicy_RequiresPolicy()
    {
        var options = new PragmaticRemoteInvokeOptions { AuthorizationPolicy = "ServiceToService" };

        RemoteInvokeEndpointAuth.Decide(options)
            .Should().Be(RemoteInvokeEndpointAuth.Mode.RequirePolicy);
    }

    [Fact]
    public void Decide_WithAllowAnonymous_IsAnonymous()
    {
        var options = new PragmaticRemoteInvokeOptions { AllowAnonymous = true };

        RemoteInvokeEndpointAuth.Decide(options)
            .Should().Be(RemoteInvokeEndpointAuth.Mode.Anonymous);
    }

    [Fact]
    public void Decide_AllowAnonymous_WinsOverPolicy()
    {
        var options = new PragmaticRemoteInvokeOptions
        {
            AllowAnonymous = true,
            AuthorizationPolicy = "ServiceToService"
        };

        RemoteInvokeEndpointAuth.Decide(options)
            .Should().Be(RemoteInvokeEndpointAuth.Mode.Anonymous);
    }

    [Fact]
    public void Resolve_WithNoConfiguration_ReturnsFailClosedDefaults()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var options = RemoteInvokeEndpointAuth.Resolve(services);

        options.AllowAnonymous.Should().BeFalse();
        options.AuthorizationPolicy.Should().BeNull();
    }

    [Fact]
    public void Resolve_BindsConfigurationSection()
    {
        var services = BuildServicesWithConfig(new Dictionary<string, string?>
        {
            ["Pragmatic:RemoteBoundaries:InvokeEndpoint:AllowAnonymous"] = "true",
            ["Pragmatic:RemoteBoundaries:InvokeEndpoint:AuthorizationPolicy"] = "ServiceToService"
        });

        var options = RemoteInvokeEndpointAuth.Resolve(services);

        options.AllowAnonymous.Should().BeTrue();
        options.AuthorizationPolicy.Should().Be("ServiceToService");
    }

    [Fact]
    public void Apply_WithDefaults_AttachesAuthorizationMetadata()
    {
        var endpoint = BuildInvokeEndpoint([]);

        endpoint.Metadata.GetMetadata<IAuthorizeData>()
            .Should().NotBeNull("the invoke endpoint must be fail-closed by default");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
    }

    [Fact]
    public void Apply_WithAllowAnonymous_AttachesAllowAnonymousMetadata()
    {
        var endpoint = BuildInvokeEndpoint(new Dictionary<string, string?>
        {
            ["Pragmatic:RemoteBoundaries:InvokeEndpoint:AllowAnonymous"] = "true"
        });

        endpoint.Metadata.GetMetadata<IAllowAnonymous>()
            .Should().NotBeNull("an explicit opt-out must allow anonymous access");
    }

    private static Endpoint BuildInvokeEndpoint(Dictionary<string, string?> config)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(config);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        var route = app.MapPost("/_pragmatic/invoke", () => Results.Ok());
        RemoteInvokeEndpointAuth.Apply(route, app.Services);

        var dataSource = ((IEndpointRouteBuilder)app).DataSources.Single();
        return dataSource.Endpoints.Single();
    }

    private static IServiceProvider BuildServicesWithConfig(Dictionary<string, string?> values)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new ServiceCollection()
            .AddSingleton(config)
            .BuildServiceProvider();
    }
}
