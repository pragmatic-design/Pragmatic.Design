using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.Configuration;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class PragmaticEndpointsOptionsTests
{
    [Fact]
    public void DefaultValues_RoutePrefixIsEmpty()
    {
        var options = new PragmaticEndpointsOptions();

        options.RoutePrefix.Should().BeEmpty();
    }

    [Fact]
    public void DefaultValues_EnableOpenApiIsTrue()
    {
        var options = new PragmaticEndpointsOptions();

        options.EnableOpenApi.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_OpenApiTitleIsApi()
    {
        var options = new PragmaticEndpointsOptions();

        options.OpenApiTitle.Should().Be("API");
    }

    [Fact]
    public void DefaultValues_UseCamelCaseJsonIsTrue()
    {
        var options = new PragmaticEndpointsOptions();

        options.UseCamelCaseJson.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_RequireAuthorizationIsTrue()
    {
        var options = new PragmaticEndpointsOptions();

        // Secure-by-default: unannotated endpoints require auth via the root group unless the host opts out.
        options.RequireAuthorizationByDefault.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_GroupsIsEmpty()
    {
        var options = new PragmaticEndpointsOptions();

        options.Groups.Should().BeEmpty();
    }

    [Fact]
    public void DefaultValues_DefaultApiVersionIsNull()
    {
        var options = new PragmaticEndpointsOptions();

        options.DefaultApiVersion.Should().BeNull();
    }

    [Fact]
    public void DefaultValues_IncludeStackTraceInErrorsIsFalse()
    {
        var options = new PragmaticEndpointsOptions();

        options.IncludeStackTraceInErrors.Should().BeFalse();
    }

    [Fact]
    public void DefaultValues_EnableResponseCompressionIsFalse()
    {
        var options = new PragmaticEndpointsOptions();

        options.EnableResponseCompression.Should().BeFalse();
    }

    [Fact]
    public void ConfigureGroup_CreatesNewGroup()
    {
        var options = new PragmaticEndpointsOptions();

        options.ConfigureGroup("Orders", g => g.RoutePrefix = "/orders");

        options.Groups.Should().ContainKey("Orders");
        options.Groups["Orders"].RoutePrefix.Should().Be("/orders");
    }

    [Fact]
    public void ConfigureGroup_ReturnsSameInstance_ForChaining()
    {
        var options = new PragmaticEndpointsOptions();

        var returned = options.ConfigureGroup("Test", _ => { });

        returned.Should().BeSameAs(options);
    }

    [Fact]
    public void ConfigureGroup_ExistingGroup_UpdatesIt()
    {
        var options = new PragmaticEndpointsOptions();
        options.ConfigureGroup("Orders", g => g.RoutePrefix = "/orders");

        options.ConfigureGroup("Orders", g => g.RequireAuthorization = true);

        options.Groups["Orders"].RoutePrefix.Should().Be("/orders");
        options.Groups["Orders"].RequireAuthorization.Should().BeTrue();
    }

    [Fact]
    public void ConfigureGroup_CaseInsensitive()
    {
        var options = new PragmaticEndpointsOptions();
        options.ConfigureGroup("ORDERS", g => g.RoutePrefix = "/orders");

        options.Groups.Should().ContainKey("orders");
        options.Groups.Should().ContainKey("Orders");
    }

    [Fact]
    public void EndpointGroupOptions_DefaultValues()
    {
        var group = new EndpointGroupOptions();

        group.RoutePrefix.Should().BeNull();
        group.Tags.Should().BeEmpty();
        group.RequireAuthorization.Should().BeFalse();
        group.AuthorizationPolicy.Should().BeNull();
        group.RequiredPermissions.Should().BeEmpty();
        group.Version.Should().BeNull();
        group.RateLimitPolicy.Should().BeNull();
        group.ResponseCacheDuration.Should().BeNull();
        group.EnableCors.Should().BeFalse();
        group.CorsPolicy.Should().BeNull();
    }

    [Fact]
    public void DefaultValues_OpenApiVersionIsDefault()
    {
        var options = new PragmaticEndpointsOptions();

        options.OpenApiVersion.Should().Be("1.0.0");
    }
}
