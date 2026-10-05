using Microsoft.AspNetCore.Http;
using Pragmatic.Logging.AspNetCore;
using Pragmatic.Logging.Context;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     The built-in request providers describe the request being served when the properties are read,
///     not the first request the context manager saw.
/// </summary>
/// <remarks>
///     Like the thread provider, they were cached with the rest of the aggregate, availability included.
///     A first read outside a request left every later entry without request properties. A first read
///     inside one stamped that request's path and correlation id on everything after it.
/// </remarks>
public class RequestContextIsReadPerCallTests
{
    [Fact]
    public void TwoRequests_EachReadCarriesItsOwnRequest()
    {
        var accessor = new HttpContextAccessor();
        using var manager = new ContextManager(registerDefaultProviders: false);
        manager.RegisterProvider(new HttpContextProvider(accessor));
        manager.RegisterProvider(new CorrelationIdProvider(accessor));

        accessor.HttpContext = Request("/orders", "corr-1");
        var first = manager.GetContextProperties();

        accessor.HttpContext = Request("/invoices", "corr-2");
        var second = manager.GetContextProperties();

        first["RequestPath"].Should().Be("/orders");
        first["CorrelationId"].Should().Be("corr-1");
        second["RequestPath"].Should().Be("/invoices");
        second["CorrelationId"].Should().Be("corr-2");
    }

    [Fact]
    public void AFirstReadOutsideARequest_DoesNotHideTheRequestsAfterIt()
    {
        var accessor = new HttpContextAccessor();
        using var manager = new ContextManager(registerDefaultProviders: false);
        manager.RegisterProvider(new HttpContextProvider(accessor));

        manager.GetContextProperties().Should().NotContainKey("RequestPath");

        accessor.HttpContext = Request("/orders", "corr-1");
        manager.GetContextProperties()["RequestPath"].Should().Be("/orders");
    }

    private static DefaultHttpContext Request(string path, string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Headers[CorrelationIdProvider.CorrelationIdHeaderName] = correlationId;
        return context;
    }
}
