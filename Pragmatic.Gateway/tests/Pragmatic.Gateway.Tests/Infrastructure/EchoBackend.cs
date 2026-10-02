using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Gateway.Tests.Infrastructure;

/// <summary>
///     A backend on a real port that answers every request with its own name and the path it received.
/// </summary>
/// <remarks>
///     A real listener and not a test server: YARP forwards over its own HTTP client, so a backend the
///     gateway can reach has to be one a socket can reach. The name says which destination answered, and
///     the path says what the gateway forwarded — which is what a route's backends and its transforms are
///     about.
/// </remarks>
internal sealed class EchoBackend : IAsyncDisposable
{
    private readonly WebApplication _app;

    private EchoBackend(string name, WebApplication app, string address)
    {
        Name = name;
        _app = app;
        Address = address;
    }

    /// <summary>What this backend answers with, first.</summary>
    public string Name { get; }

    /// <summary>The address it listens on, as a gateway route names it.</summary>
    public string Address { get; }

    /// <summary>Starts a backend on a free loopback port.</summary>
    public static async Task<EchoBackend> StartAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.Run(context => context.Response.WriteAsync(
            $"{name} {context.Request.Path}{context.Request.QueryString}"));

        await app.StartAsync().ConfigureAwait(false);

        return new EchoBackend(name, app, app.Urls.Single());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
