// Pragmatic.Composition.HostWiring.Tests - A background service that fails once the host is serving

using System.Net;
using System.Text.RegularExpressions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A background service that fails after startup ends the process; it does not put a serving host in
///     maintenance mode for a startup failure that never happened. A startup failure still does.
/// </summary>
/// <remarks>
///     <para>
///         The generated <c>PragmaticApp.RunAsync</c> catches what fails during startup and serves the
///         maintenance page instead. On .NET 11, <c>IHost</c>'s wait for shutdown rethrows a failed worker's
///         exception (on .NET 10 it returns once the host has stopped), so an <c>await app.RunAsync()</c>
///         inside that <c>try</c> took a worker failing hours later for a failed startup: 503 on every route,
///         "failed to start" on <c>/health</c>, and a process that never exits for the orchestrator to restart.
///     </para>
///     <para>
///         The generated host runs as a child process, on each runtime in turn, because that is the only
///         place the difference exists: the suite's own process runs on one runtime, and in-process the
///         maintenance app would be a second web host in the test runner. The host prints the runtime it
///         got, so a machine without the 11 runtime fails here instead of passing on 10 twice.
///     </para>
/// </remarks>
public sealed partial class AWorkerThatFailsAfterStartIsNotAStartupFailureTests
{
    /// <summary>
    ///     The name every assembly this case writes into the output directory starts with, which the suite's
    ///     reference set leaves out (<see cref="HostWiringFixture.BuildReferenceSet" />).
    /// </summary>
    internal const string RunProbePrefix = "WorkerFailureProbe";

    private const string WorkerFailure = "probe worker failed after start";

    private const string Module = """
        using Pragmatic.Composition.Attributes;

        namespace Probe.Worker
        {
            [Module(Name = "ProbeWorker")]
            public sealed class ProbeWorkerModule;
        }
        """;

    // One host for both cases, picked by an argument. The worker fails on ApplicationStarted: the server is
    // listening and every hosted service has started, which is what "after startup" means, and it is a
    // signal rather than a delay. The other service fails inside StartAsync, which is a startup failure.
    private const string Host = """
        using System;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Pragmatic.Composition.Hosting;

        namespace Probe.Host;

        public static class Program
        {
            public static Task Main(string[] args)
            {
                Console.WriteLine($"runtime {Environment.Version}");
                var failAtStart = args.Contains("--fail-at-start");
                return PragmaticApp.RunAsync(args, app =>
                {
                    if (failAtStart)
                        app.Services.AddHostedService<FailingStart>();
                    else
                        app.Services.AddHostedService<FailingWorker>();
                });
            }
        }

        public sealed class FailingWorker(IHostApplicationLifetime lifetime) : BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
                    await started.Task.WaitAsync(stoppingToken);

                throw new InvalidOperationException("probe worker failed after start");
            }
        }

        public sealed class FailingStart : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken)
                => throw new InvalidOperationException("probe failed to start");

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
        """;

    /// <summary>
    ///     How long a host gets to do what the case waits for. Each does it in seconds; at the bound, the
    ///     process is killed and the case reports what it printed.
    /// </summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task OnTheElevenRuntime_TheWorkersExceptionEndsTheProcess()
    {
        var run = await RunToExitAsync(rollForwardToLatestMajor: true);

        run.Output.Should().Contain("runtime 11.", "this case is about the 11 runtime; it has to be the one that ran");
        run.Exited.Should().BeTrue("a host still running is the maintenance app, which no orchestrator restarts:\n" + run.Output);
        run.ExitCode.Should().NotBe(0, "the worker's failure propagates out of PragmaticApp.RunAsync:\n" + run.Output);
        run.Output.Should().Contain(WorkerFailure);
        run.Output.Should().NotContain("maintenance mode", "startup had succeeded:\n" + run.Output);
    }

    /// <summary>The control: on .NET 10 the host stops and the process exits by itself, as it always did.</summary>
    [Fact]
    public async Task OnTheTenRuntime_TheHostStopsAndTheProcessExitsCleanly()
    {
        var run = await RunToExitAsync(rollForwardToLatestMajor: false);

        run.Output.Should().Contain("runtime 10.", "this case is about the 10 runtime; it has to be the one that ran");
        run.Exited.Should().BeTrue(run.Output);
        run.ExitCode.Should().Be(0, "the wait for shutdown returns normally on .NET 10 once a failed worker has stopped the host:\n" + run.Output);
        run.Output.Should().Contain(WorkerFailure, "the host logs the worker's failure");
        run.Output.Should().NotContain("maintenance mode");
    }

    /// <summary>
    ///     What the <c>catch</c> is for, on each runtime: a hosted service that fails to start puts the host in
    ///     maintenance, and the maintenance app answers 503 where the host was told to listen.
    /// </summary>
    [Theory]
    [InlineData(false, "runtime 10.")]
    [InlineData(true, "runtime 11.")]
    public async Task AStartupFailure_StillServesTheMaintenancePage(bool rollForwardToLatestMajor, string runtime)
    {
        var host = await StartHostAsync(rollForwardToLatestMajor, "--fail-at-start");
        await using var _ = host.ConfigureAwait(false);
        using var bound = new CancellationTokenSource(Bound);

        var printed = new List<string>();
        Uri? maintenance = null;
        while (maintenance is null && await host.Process.StandardOutput.ReadLineAsync(bound.Token) is { } line)
        {
            printed.Add(line);
            if (MaintenanceAddress().Match(line) is { Success: true } match)
                maintenance = new Uri(match.Groups["url"].Value);
        }

        var output = string.Join('\n', printed);
        output.Should().Contain(runtime);
        maintenance.Should().NotBeNull("a startup failure enters maintenance mode and says where it answers:\n" + output);

        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(maintenance!, "/"), bound.Token);
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [GeneratedRegex(@"maintenance mode and answers 503 on (?<url>http://[^\s]+?)\.(\s|$)")]
    private static partial Regex MaintenanceAddress();

    private sealed record HostRun(bool Exited, int ExitCode, string Output);

    private static async Task<HostRun> RunToExitAsync(bool rollForwardToLatestMajor)
    {
        var host = await StartHostAsync(rollForwardToLatestMajor).ConfigureAwait(false);
        await using var _ = host.ConfigureAwait(false);
        var stdout = host.Process.StandardOutput.ReadToEndAsync();
        var stderr = host.Process.StandardError.ReadToEndAsync();

        using var bound = new CancellationTokenSource(Bound);
        var exited = true;
        try
        {
            await host.Process.WaitForExitAsync(bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            exited = false;
            await host.StopAsync().ConfigureAwait(false);
        }

        var output = $"{await stdout.ConfigureAwait(false)}\n{await stderr.ConfigureAwait(false)}";
        return new HostRun(exited, exited ? host.Process.ExitCode : 0, output);
    }

    private static Task<ProbeHost> StartHostAsync(bool rollForwardToLatestMajor, params string[] hostArgs)
    {
        var name = $"{RunProbePrefix}{Guid.NewGuid():N}";
        var (errors, module, host) = ModuleAndHost.Emit($"{name}.Module", [Module], $"{name}.Host", Host);
        errors.Should().BeEmpty("the host has to compile, or there is nothing to run");

        return ProbeHost.StartAsync(name, module, host!, rollForwardToLatestMajor, hostArgs);
    }
}
