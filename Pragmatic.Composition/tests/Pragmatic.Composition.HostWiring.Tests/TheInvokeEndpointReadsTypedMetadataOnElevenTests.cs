// Pragmatic.Composition.HostWiring.Tests - The invoke endpoint of a .NET 11 host reads its request through GetTypeInfo<T>()

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host compiled for .NET 11 reads the request of <c>POST /_pragmatic/invoke</c> through
///     <c>JsonSerializerOptions.GetTypeInfo&lt;T&gt;()</c>, and the call runs: a request goes in, the action's answer comes out (#838).
/// </summary>
/// <remarks>
///     <para>
///         The generator writes the typed call where the consumer's System.Text.Json has it, and the cast it always wrote where it does
///         not (#834). A snapshot shows that the call is written; only a host that serves the request shows that it runs. So the host is
///         compiled against the 11 shared frameworks and run on the 11 runtime, as a process of its own, and the same module and host,
///         compiled against the 10 frameworks and run on 10, is the control.
///     </para>
///     <para>
///         The host prints the runtime it got and the address it listens on once it has started: a signal, not a delay.
///     </para>
/// </remarks>
public sealed partial class TheInvokeEndpointReadsTypedMetadataOnElevenTests
{
    /// <summary>The name every assembly this case writes into the output directory starts with (see <see cref="ProbeHost" />).</summary>
    internal const string RunProbePrefix = "InvokeProbe";

    private const string TypedRead = "var __requestInfo = __jsonOptions.GetTypeInfo<PragmaticInvokeRequest>();";
    private const string CastRead = "__jsonOptions.GetTypeInfo(typeof(PragmaticInvokeRequest));";

    private const string Module = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        // No [Boundary]: a boundary's invoker takes the unit of work of a database, and this action writes nothing.
        namespace Echo
        {
            [Module(Name = "Echo")]
            public sealed class EchoModule;
        }

        namespace Echo.Actions
        {
            // The route is not what the case calls: a host maps the dispatcher only beside endpoints of its own.
            [DomainAction]
            [Endpoint(HttpVerb.Post, "api/shout")]
            public partial class Shout : DomainAction<string>
            {
                public required string Text { get; init; }

                public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<string, IError>.Success(Text.ToUpperInvariant()));
            }
        }
        """;

    private const string Host = """
        using System;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Hosting.Server;
        using Microsoft.AspNetCore.Hosting.Server.Features;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Composition.Hosting;

        namespace Probe.Host;

        // No users: the case calls the dispatcher, which the configuration opens to anonymous callers.
        [Module]
        [Include<Echo.EchoModule>]
        [AnonymousHost]
        public sealed class EchoHostModule;

        public static class Program
        {
            public static Task Main(string[] args)
            {
                Console.WriteLine($"runtime {Environment.Version}");
                return PragmaticApp.RunAsync(args, app => app.Services.AddHostedService<PrintAddress>());
            }
        }

        public sealed class PrintAddress(IHostApplicationLifetime lifetime, IServer server) : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken)
            {
                lifetime.ApplicationStarted.Register(() =>
                    Console.WriteLine($"listening on {server.Features.Get<IServerAddressesFeature>()!.Addresses.First()}"));
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
        """;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    [Theory]
    [InlineData(11, true, "runtime 11.", TypedRead, CastRead)]
    [InlineData(null, false, "runtime 10.", CastRead, TypedRead)]
    public async Task AnInvokeRequest_IsReadAndAnswered(int? frameworkMajor, bool rollForwardToLatestMajor, string runtime, string written, string notWritten)
    {
        var name = $"{RunProbePrefix}{Guid.NewGuid():N}";

        var (generationErrors, generated) = ModuleAndHost.Generate(
            $"{name}.Module", [Module], $"{name}.Host", Host, frameworkMajor: frameworkMajor);
        generationErrors.Should().BeEmpty();
        var endpoint = generated["_Infra.Remote.InvokeEndpoint.g.cs"];
        endpoint.Should().Contain(written);
        endpoint.Should().NotContain(notWritten);

        var (errors, module, host) = ModuleAndHost.Emit(
            $"{name}.Module", [Module], $"{name}.Host", Host, frameworkMajor: frameworkMajor);
        errors.Should().BeEmpty("the host has to compile, or there is nothing to run");

        var running = await ProbeHost.StartAsync(name, module, host!, rollForwardToLatestMajor,
            "--Pragmatic:RemoteBoundaries:InvokeEndpoint:Enabled=true",
            "--Pragmatic:RemoteBoundaries:InvokeEndpoint:AllowAnonymous=true",
            // A startup failure ends the process, and so the wait for the address, with the reason printed.
            "--Pragmatic:MaintenanceMode:EnableOnStartupFailure=false");
        await using var _ = running.ConfigureAwait(true);
        using var bound = new CancellationTokenSource(Bound);

        // Both pipes are drained for the whole run: a host whose log fills an unread pipe blocks on its next write, request or not.
        var errorOutput = running.Process.StandardError.ReadToEndAsync();

        var printed = new List<string>();
        Uri? address = null;
        try
        {
            while (address is null && await running.Process.StandardOutput.ReadLineAsync(bound.Token).ConfigureAwait(true) is { } line)
            {
                printed.Add(line);
                if (ListeningAddress().Match(line) is { Success: true } match)
                    address = new Uri(match.Groups["url"].Value);
            }
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            // A host that never says where it listens: what it did print is the case's report, below.
            printed.Add($"(no address within {Bound})");
        }

        var output = string.Join('\n', printed);
        if (address is null)
        {
            await running.StopAsync().ConfigureAwait(true);
            output += "\n" + await errorOutput.ConfigureAwait(true);
        }

        output.Should().Contain(runtime, "this case is about that runtime; it has to be the one that ran");
        address.Should().NotBeNull("the host starts and says where it listens:\n" + output);
        var restOfOutput = running.Process.StandardOutput.ReadToEndAsync();

        using var client = new HttpClient();
        using var request = new StringContent(
            """{"actionType":"Echo.Actions.Shout","payload":{"Text":"hello"}}""", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(new Uri(address!, "/_pragmatic/invoke"), request, bound.Token).ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync(bound.Token).ConfigureAwait(true);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            await running.StopAsync().ConfigureAwait(true);
            output += "\n" + await restOfOutput.ConfigureAwait(true) + "\n" + await errorOutput.ConfigureAwait(true);
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK, body + "\n" + output);
        body.Should().Contain("HELLO", "the action ran on the request the endpoint read");
    }

    [GeneratedRegex(@"listening on (?<url>http://\S+)")]
    private static partial Regex ListeningAddress();
}
