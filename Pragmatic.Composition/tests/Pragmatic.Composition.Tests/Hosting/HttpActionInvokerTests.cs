using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Composition.Remote;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     A remote boundary that is unreachable, times out, or returns a
///     non-JSON body must be surfaced as <c>Result.Failure(RemoteError)</c> — never propagate as an
///     exception (Result over exceptions). Caller-initiated cancellation still propagates.
/// </summary>
public class HttpActionInvokerTests
{
    private sealed class TestAction : DomainAction<string>
    {
        public string Name { get; init; } = "";
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => throw new NotSupportedException("never invoked locally in these tests");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request, ct));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = baseAddress };
    }

    private static HttpActionInvoker<TestAction, string> InvokerFor(HttpMessageHandler handler)
        => new(new StubHttpClientFactory(handler, new Uri("http://remote.invalid")), "remote");

    [Fact]
    public async Task InvokeAsync_WhenBaseUrlNotConfigured_ReturnsRemoteError_DoesNotThrow()
    {
        // BaseAddress null (unconfigured remote boundary) → Result, not InvalidOperationException (REMOTE-NOBASEURL).
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var invoker = new HttpActionInvoker<TestAction, string>(new StubHttpClientFactory(handler, baseAddress: null), "remote");

        var result = await invoker.InvokeAsync(new TestAction { Name = "x" });

        result.IsFailure.Should().BeTrue();
        result.TryGetError(out var error).Should().BeTrue();
        error.Should().BeOfType<RemoteError>().Which.Code.Should().Be("REMOTE_NO_BASE_URL");
    }

    [Fact]
    public async Task InvokeAsync_WhenBoundaryUnreachable_ReturnsRemoteError_DoesNotThrow()
    {
        var invoker = InvokerFor(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));

        var result = await invoker.InvokeAsync(new TestAction { Name = "x" });

        result.IsFailure.Should().BeTrue();
        result.TryGetError(out var error).Should().BeTrue();
        error.Should().BeOfType<RemoteError>().Which.Code.Should().Be("REMOTE_TRANSPORT_FAILED");
    }

    [Fact]
    public async Task InvokeAsync_WhenResponseIsNotJson_ReturnsRemoteError_DoesNotThrow()
    {
        var invoker = InvokerFor(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>502 Bad Gateway</html>", Encoding.UTF8, "text/html")
        }));

        var result = await invoker.InvokeAsync(new TestAction { Name = "x" });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenCallerCancels_PropagatesCancellation()
    {
        var invoker = InvokerFor(new StubHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => invoker.InvokeAsync(new TestAction { Name = "x" }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
