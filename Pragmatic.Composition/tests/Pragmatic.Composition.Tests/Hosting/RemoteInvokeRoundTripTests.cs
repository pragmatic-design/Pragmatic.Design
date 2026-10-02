using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Composition.Remote;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     Full remote round-trip (closes the REMOTE test gap): HttpActionInvoker serializes an action →
///     the <c>/_pragmatic/invoke</c> endpoint deserializes it and dispatches via PragmaticInvokeDispatcher
///     → the response is serialized → HttpActionInvoker deserializes it back to a Result. Exercises the
///     real client↔server JSON compatibility (not just the invoker in isolation).
/// </summary>
public class RemoteInvokeRoundTripTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public sealed class EchoAction : DomainAction<EchoResult>
    {
        public string Name { get; init; } = "";
        public override Task<Result<EchoResult, IError>> Execute(CancellationToken ct = default)
            => throw new NotSupportedException("dispatched remotely in this test");
    }

    public sealed record EchoResult(string Value);

    // Server-side invoker: mirrors what a real boundary would do — return a Result the dispatcher serializes.
    private sealed class ServerEchoInvoker : IDomainActionInvoker<EchoAction, EchoResult>
    {
        public Task<Result<EchoResult, IError>> InvokeAsync(EchoAction action, CancellationToken ct = default)
            => Task.FromResult(action.Name == "boom"
                ? Result<EchoResult, IError>.Failure(new RemoteError { Code = "BOOM", StatusCode = 422, Title = "Boom" })
                : Result<EchoResult, IError>.Success(new EchoResult($"echo:{action.Name}")));
    }

    public async Task InitializeAsync()
    {
        _app = WebApplication.CreateSlimBuilder().Build();
        _app.Urls.Add("http://127.0.0.1:0");

        _app.MapPost("/_pragmatic/invoke", async (HttpContext ctx) =>
        {
            var request = await ctx.Request.ReadFromJsonAsync<PragmaticInvokeRequest>(ctx.RequestAborted).ConfigureAwait(false);
            // The generated endpoint deserializes the payload into the closed action type (default options).
            var action = request!.Payload.Deserialize<EchoAction>()!;
            return await PragmaticInvokeDispatcher.InvokeActionAsync(action, new ServerEchoInvoker(), ctx.RequestAborted).ConfigureAwait(false);
        });

        await _app.StartAsync().ConfigureAwait(false);

        var address = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private HttpActionInvoker<EchoAction, EchoResult> Invoker()
        => new(new SingleClientFactory(_client), "remote");

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task Invoke_SuccessAction_RoundTripsResultValue()
    {
        var result = await Invoker().InvokeAsync(new EchoAction { Name = "hello" });

        result.IsSuccess.Should().BeTrue();
        result.Match(v => v.Value, _ => (string?)null).Should().Be("echo:hello");
    }

    [Fact]
    public async Task Invoke_FailureAction_RoundTripsRemoteError()
    {
        var result = await Invoker().InvokeAsync(new EchoAction { Name = "boom" });

        result.IsFailure.Should().BeTrue();
        var error = result.Match<IError?>(_ => null, e => e);
        error.Should().BeOfType<RemoteError>();
        error!.StatusCode.Should().Be(422);
    }
}
