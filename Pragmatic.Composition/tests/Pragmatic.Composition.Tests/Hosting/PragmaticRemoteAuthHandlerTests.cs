using System.Net;
using System.Net.Http.Headers;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Pragmatic.Composition.Remote;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     The remote-client handler propagates the caller's identity (Authorization header)
///     onto the outbound <c>/_pragmatic/invoke</c> call so the remote host can authenticate it.
/// </summary>
public class PragmaticRemoteAuthHandlerTests
{
    [Fact]
    public async Task SendAsync_WithInboundAuthorization_ForwardsHeader()
    {
        var accessor = new HttpContextAccessor { HttpContext = ContextWithAuthorization("Bearer abc123") };
        var (invoker, capture) = CreateInvoker(accessor);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/_pragmatic/invoke"), default);

        capture.Captured!.Headers.Authorization.Should().NotBeNull();
        capture.Captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capture.Captured.Headers.Authorization.Parameter.Should().Be("abc123");
    }

    [Fact]
    public async Task SendAsync_WithNoHttpContext_DoesNotSetHeader()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var (invoker, capture) = CreateInvoker(accessor);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/_pragmatic/invoke"), default);

        capture.Captured!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WithExplicitOutboundAuthorization_DoesNotOverwrite()
    {
        var accessor = new HttpContextAccessor { HttpContext = ContextWithAuthorization("Bearer inbound") };
        var (invoker, capture) = CreateInvoker(accessor);

        var request = new HttpRequestMessage(HttpMethod.Post, "/_pragmatic/invoke")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "explicit") }
        };

        await invoker.SendAsync(request, default);

        capture.Captured!.Headers.Authorization!.Parameter.Should().Be("explicit");
    }

    private static (HttpMessageInvoker invoker, CapturingHandler capture) CreateInvoker(
        IHttpContextAccessor accessor)
    {
        var capture = new CapturingHandler();
        var handler = new PragmaticRemoteAuthHandler(accessor) { InnerHandler = capture };
        return (new HttpMessageInvoker(handler), capture);
    }

    private static DefaultHttpContext ContextWithAuthorization(string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = value;
        return context;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
