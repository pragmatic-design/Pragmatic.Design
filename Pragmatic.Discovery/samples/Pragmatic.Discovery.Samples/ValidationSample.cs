// Pragmatic.Discovery Samples - DISC001 / DISC002 / DISC003 validation enumeration.

using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates topology validation: registers conflicting hosts and enumerates the
/// resulting <see cref="DiscoveryValidationIssue"/> values (DISC001/DISC002/DISC003).
/// </summary>
internal static class ValidationSample
{
    public static async Task RunAsync()
    {
        SampleConsole.Header("Validation — DISC001 / DISC002 / DISC003");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDiscovery(o =>
        {
            o.AutoRegisterOnStartup = false;
            o.ValidateOnStartup = false;
        });
        await using var provider = services.BuildServiceProvider();
        var discovery = provider.GetRequiredService<IDiscoveryService>();

        // --- DISC001: same module deployed on two different databases (Info, allowed scale-out) ---
        await discovery.RegisterAsync(new HostTopologyInfo
        {
            HostName = "Reports.Host",
            Modules = [new ModuleDeploymentInfo { ModuleName = "BillingModule", DatabaseName = "ReportsDatabase", Provider = "PostgreSQL" }],
        });
        var disc001 = await discovery.ValidateAsync(SampleData.BillingHost()); // BillingModule on a different DB
        Report("DISC001 (same module, different database)", disc001);

        // --- DISC002: same module + same database but different provider (Warning, likely misconfig) ---
        var sharedDbA = new HostTopologyInfo
        {
            HostName = "Inventory.Host.A",
            Modules = [new ModuleDeploymentInfo { ModuleName = "InventoryModule", DatabaseName = "SharedDatabase", Provider = "PostgreSQL" }],
        };
        var sharedDbB = new HostTopologyInfo
        {
            HostName = "Inventory.Host.B",
            Modules = [new ModuleDeploymentInfo { ModuleName = "InventoryModule", DatabaseName = "SharedDatabase", Provider = "SqlServer" }],
        };
        await discovery.RegisterAsync(sharedDbA);
        var disc002 = await discovery.ValidateAsync(sharedDbB);
        Report("DISC002 (same database, different provider)", disc002);

        // --- DISC003: boundary ReadAccess on an entity whose owning module is not visible (Info) ---
        var orphanReadAccess = new HostTopologyInfo
        {
            HostName = "Orphan.Host",
            Modules = [new ModuleDeploymentInfo { ModuleName = "ShippingModule", DatabaseName = "ShippingDatabase", Provider = "PostgreSQL" }],
            // Entity name does not start with any known module name → unresolved advisory.
            Boundaries = [new BoundaryReadAccessInfo { BoundaryName = "Shipping", EntityTypes = ["ZZZUnknownEntity"] }],
        };
        var disc003 = await discovery.ValidateAsync(orphanReadAccess);
        Report("DISC003 (ReadAccess entity with no visible owning module)", disc003);

        SampleConsole.Blank();
    }

    private static void Report(string scenario, DiscoveryValidationResult result)
    {
        SampleConsole.Step($"{scenario}: IsValid={result.IsValid}, Issues={result.Issues.Count}");
        foreach (var issue in result.Issues)
            SampleConsole.Info($"[{issue.Severity}] {issue.Code}: {Truncate(issue.Message, 90)}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
