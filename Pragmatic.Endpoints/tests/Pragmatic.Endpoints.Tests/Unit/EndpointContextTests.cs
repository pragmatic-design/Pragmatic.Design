using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Pragmatic.Endpoints.Context;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class EndpointContextTests
{
    private static EndpointContext CreateContext(HttpContext? httpContext = null, string name = "TestEndpoint")
    {
        var http = httpContext ?? new DefaultHttpContext();
        var endpoint = new object();
        return new EndpointContext(http, name, endpoint);
    }

    [Fact]
    public void Constructor_SetsEndpointName()
    {
        var context = CreateContext(name: "GetUser");

        context.EndpointName.Should().Be("GetUser");
    }

    [Fact]
    public void Constructor_SetsEndpoint()
    {
        var endpoint = new object();
        var http = new DefaultHttpContext();
        var context = new EndpointContext(http, "Test", endpoint);

        context.Endpoint.Should().BeSameAs(endpoint);
    }

    [Fact]
    public void Constructor_SetsHttpContext()
    {
        var http = new DefaultHttpContext();
        var context = new EndpointContext(http, "Test", new object());

        context.HttpContext.Should().BeSameAs(http);
    }

    [Fact]
    public void User_ReturnsHttpContextUser()
    {
        var http = new DefaultHttpContext();
        var context = CreateContext(http);

        context.User.Should().BeSameAs(http.User);
    }

    [Fact]
    public void Request_ReturnsHttpContextRequest()
    {
        var http = new DefaultHttpContext();
        var context = CreateContext(http);

        context.Request.Should().BeSameAs(http.Request);
    }

    [Fact]
    public void Response_ReturnsHttpContextResponse()
    {
        var http = new DefaultHttpContext();
        var context = CreateContext(http);

        context.Response.Should().BeSameAs(http.Response);
    }

    [Fact]
    public void CancellationToken_ReturnsRequestAborted()
    {
        var http = new DefaultHttpContext();
        using var cts = new CancellationTokenSource();
        http.RequestAborted = cts.Token;
        var context = CreateContext(http);

        context.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void Items_SetAndGet_ReturnsValue()
    {
        var context = CreateContext();

        context["myKey"] = "myValue";

        context["myKey"].Should().Be("myValue");
    }

    [Fact]
    public void Items_GetMissing_ReturnsNull()
    {
        var context = CreateContext();

        context["nonExistent"].Should().BeNull();
    }

    [Fact]
    public void GetHeader_ExistingHeader_ReturnsValue()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Custom"] = "test-value";
        var context = CreateContext(http);

        context.GetHeader("X-Custom").Should().Be("test-value");
    }

    [Fact]
    public void GetHeader_MissingHeader_ReturnsNull()
    {
        var context = CreateContext();

        context.GetHeader("X-Missing").Should().BeNull();
    }

    [Fact]
    public void SetResponseHeader_SetsHeader()
    {
        var http = new DefaultHttpContext();
        var context = CreateContext(http);

        context.SetResponseHeader("X-Response", "value");

        http.Response.Headers["X-Response"].ToString().Should().Be("value");
    }

    [Fact]
    public void GetRouteValue_ValidInt_ReturnsConverted()
    {
        var http = new DefaultHttpContext();
        http.Request.RouteValues["id"] = "42";
        var context = CreateContext(http);

        var value = context.GetRouteValue<int>("id");

        value.Should().Be(42);
    }

    [Fact]
    public void GetRouteValue_Missing_ReturnsDefault()
    {
        var context = CreateContext();

        var value = context.GetRouteValue<int>("missing");

        value.Should().Be(0);
    }

    [Fact]
    public void GetRouteValue_InvalidConversion_ReturnsDefault()
    {
        var http = new DefaultHttpContext();
        http.Request.RouteValues["id"] = "not-a-number";
        var context = CreateContext(http);

        var value = context.GetRouteValue<int>("id");

        value.Should().Be(0);
    }

    [Fact]
    public void GetQueryValue_ExistingKey_ReturnsConverted()
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?page=5");
        var context = CreateContext(http);

        var value = context.GetQueryValue<int>("page");

        value.Should().Be(5);
    }

    [Fact]
    public void GetQueryValue_Missing_ReturnsDefault()
    {
        var context = CreateContext();

        var value = context.GetQueryValue<int>("missing");

        value.Should().Be(0);
    }
}
