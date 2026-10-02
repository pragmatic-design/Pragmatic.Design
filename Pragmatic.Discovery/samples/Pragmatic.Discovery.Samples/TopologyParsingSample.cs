// Pragmatic.Discovery Samples - HostTopologyInfo parsing (Parse / FromRegistry / FromAssembly / FromEntryAssembly).

using System.Reflection;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates how a host's topology is parsed from the SG-emitted metadata JSON, and the three
/// discovery entry points: <see cref="HostTopologyInfo.FromRegistry"/> (zero-reflection),
/// <see cref="HostTopologyInfo.FromAssembly"/> (reflection) and
/// <see cref="HostTopologyInfo.FromEntryAssembly"/> (registry first, reflection fallback).
/// </summary>
internal static class TopologyParsingSample
{
    private const string TopologyJson =
        """
        {
          "host": "Showcase.Host",
          "includes": [
            { "module": "CatalogModule", "database": "ShowcaseDatabase", "provider": "PostgreSQL" },
            { "module": "BillingModule", "database": "ShowcaseFinancialDatabase", "provider": "PostgreSQL" }
          ],
          "boundaries": [
            { "name": "Booking", "readAccess": ["CatalogProperty", "CatalogRoomType"] }
          ]
        }
        """;

    public static void Run()
    {
        SampleConsole.Header("HostTopologyInfo — Topology Parsing");

        // Parse: the core routine the SG metadata flows through.
        var topology = HostTopologyInfo.Parse(TopologyJson);
        if (topology is not null)
        {
            SampleConsole.Step($"Parse(json) → host '{topology.HostName}'");
            foreach (var m in topology.Modules)
                SampleConsole.Info($"module {m.ModuleName} → db={m.DatabaseName}, provider={m.Provider}");
            foreach (var b in topology.Boundaries)
                SampleConsole.Info($"boundary {b.BoundaryName} reads [{string.Join(", ", b.EntityTypes)}]");
        }

        // Malformed / empty JSON parses to null (callers degrade gracefully).
        SampleConsole.Step($"Parse(\"\") → {(HostTopologyInfo.Parse("") is null ? "null" : "non-null")}");
        SampleConsole.Info($"Parse(\"{{ not json\") → {(HostTopologyInfo.Parse("{ not json") is null ? "null" : "non-null")}");

        // FromRegistry: zero-reflection path. Returns null when no provider populated the registry
        // (this samples assembly has no SG-registered HostTopology entry).
        var fromRegistry = HostTopologyInfo.FromRegistry();
        SampleConsole.Step($"FromRegistry() → {(fromRegistry is null ? "null (no SG registry entry)" : fromRegistry.HostName)}");

        // FromAssembly: reflection over [assembly: PragmaticMetadata(HostTopology, ...)].
        // The samples assembly carries no such attribute, so this is null too.
        var fromAssembly = HostTopologyInfo.FromAssembly(Assembly.GetExecutingAssembly());
        SampleConsole.Step($"FromAssembly(thisAssembly) → {(fromAssembly is null ? "null (no metadata attribute)" : fromAssembly.HostName)}");

        // FromEntryAssembly: registry first, then reflection fallback. Used by the hosted service.
        var fromEntry = HostTopologyInfo.FromEntryAssembly();
        SampleConsole.Step($"FromEntryAssembly() → {(fromEntry is null ? "null (no topology metadata)" : fromEntry.HostName)}");

        SampleConsole.Info("In a real host these return the SG-emitted topology automatically.");
        SampleConsole.Blank();
    }
}
