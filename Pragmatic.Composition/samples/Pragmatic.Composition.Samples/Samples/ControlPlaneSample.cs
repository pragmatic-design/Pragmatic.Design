using System.Text.Json;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     ControlPlane integration types (<see cref="Pragmatic.ControlPlane" />): host identity/status,
///     health contributors and aggregation, and polymorphic <see cref="HostCommand" />s.
///     These are the public contracts a distributed deployment exchanges via the control plane;
///     the data types and command (de)serialization are demonstrated live (no transport required).
/// </summary>
public static class ControlPlaneSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. ControlPlane Integration Types");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  Default IControlPlane is NoOpControlPlane (monolith): IsConnected=false,");
        Console.WriteLine("  GetAllHostsAsync() returns only self. UseAgent() swaps in distributed mode.");
        Console.WriteLine();

        // A host snapshot as the control plane sees it (heartbeat payload).
        var host = new HostInfo
        {
            HostId = "01J9ZK7Q",
            HostName = "Showcase.Host",
            HostType = HostType.Tenant,
            State = HostState.Ready,
            LastHeartbeat = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-12),
            HealthStatus = ContributorHealthStatus.Healthy,
        };
        Console.WriteLine("  HostInfo (heartbeat snapshot):");
        Console.WriteLine($"    {host.HostName} [{host.HostType}] state={host.State} health={host.HealthStatus}");
        Console.WriteLine();

        // Health contributors report into an aggregate; OverallStatus = worst-case.
        var contributors = new Dictionary<string, ContributorHealthReport>
        {
            ["Database.Booking"] = ContributorHealthReport.Healthy("Connected, 4ms latency"),
            ["Transport.RabbitMq"] = ContributorHealthReport.Degraded(
                "Reconnecting",
                new Dictionary<string, object> { ["retries"] = 2 }),
            ["Cache.Hybrid"] = ContributorHealthReport.Healthy(),
        };
        var overall = contributors.Values
            .Select(r => r.Status)
            .Aggregate(ContributorHealthStatus.Healthy, (worst, s) => s > worst ? s : worst);
        var aggregate = new AggregatedHealthReport
        {
            OverallStatus = overall,
            Contributors = contributors,
        };

        Console.WriteLine("  AggregatedHealthReport (IHostHealthContributor -> IHostHealthAggregator):");
        foreach (var (name, report) in aggregate.Contributors)
            Console.WriteLine($"    {name,-22} {report.Status,-9} {report.Message}");
        Console.WriteLine($"    => OverallStatus = {aggregate.OverallStatus} (worst-case across contributors)");
        Console.WriteLine();

        Console.WriteLine("  HealthContributorMode: Pull (aggregator calls CheckAsync) vs");
        Console.WriteLine("    Push (contributor updates state proactively, CheckAsync returns cached).");
        Console.WriteLine();

        // Commands are polymorphic with a $type discriminator — no runtime reflection needed.
        Console.WriteLine("  HostCommand polymorphic serialization (what IHostCommandDispatcher consumes):");
        Console.WriteLine("  ───────────────────────────────────────────────────────────────────────────");
        HostCommand[] commands =
        [
            new EnterMaintenanceCommand("Scheduled deploy", TimeSpan.FromMinutes(5)),
            new MigrateCommand(DatabaseFilter: "BillingDb"),
            new DrainCommand(GracePeriod: TimeSpan.FromSeconds(30)),
            new ExitMaintenanceCommand(),
        ];

        foreach (var command in commands)
        {
            var json = JsonSerializer.Serialize(command);
            // Round-trip back to the base type via the $type discriminator.
            var roundTripped = JsonSerializer.Deserialize<HostCommand>(json);
            Console.WriteLine($"    {command.CommandTypeName,-26} -> {json}");
            Console.WriteLine($"      round-trip type = {roundTripped!.GetType().Name}, id = {roundTripped.CommandId[..8]}…");
        }
        Console.WriteLine();

        // Operation failures surface as a Result IError.
        var error = ControlPlaneError.NotConnected();
        var commandFailed = ControlPlaneError.CommandFailed("handler threw");
        Console.WriteLine("  ControlPlaneError (IError):");
        Console.WriteLine($"    {error.Code}: {error.Message}");
        Console.WriteLine($"    {commandFailed.Code}: {commandFailed.Message}");
        Console.WriteLine();
    }
}
