using Pragmatic.Agent.Client;
using Pragmatic.Agent.KV;
using Pragmatic.Agent.Protocol.Payloads;

namespace Pragmatic.Agent.Cli;

/// <summary>
///     Handles CLI commands received via the socket (CLI connects as a normal client).
///     CLI messages use KV operations to interact with the Agent.
///     This class provides higher-level command interpretation for the standalone CLI tool.
/// </summary>
internal static class CliHandler
{
    /// <summary>
    ///     Executes a CLI command against the local Agent.
    ///     Used when the Agent binary is invoked as a CLI tool (not as a daemon).
    /// </summary>
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        // Resolve socket path: --socket override > --instance > default
        string? socketOverride = null;
        string? instanceName = null;

        // Process flags in two passes so both --socket and --instance can be present simultaneously.
        var indicesToRemove = new HashSet<int>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--socket" && !indicesToRemove.Contains(i))
            {
                socketOverride = args[i + 1];
                indicesToRemove.Add(i);
                indicesToRemove.Add(i + 1);
            }
            else if (args[i] == "--instance" && !indicesToRemove.Contains(i))
            {
                instanceName = args[i + 1];
                indicesToRemove.Add(i);
                indicesToRemove.Add(i + 1);
            }
        }

        if (indicesToRemove.Count > 0)
            args = args.Where((_, idx) => !indicesToRemove.Contains(idx)).ToArray();

        var paths = new Configuration.AgentPaths(instanceName, socketOverride);
        var socketPath = paths.SocketPath;

        var connection = new AgentConnection(socketPath);
        await using var _ = connection.ConfigureAwait(false);

        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cannot connect to Agent at {socketPath}: {ex.Message}");
            Console.Error.WriteLine("Is the Agent daemon running?");
            return 1;
        }

        // The daemon answers a KV request only from a registered client. Unregistered, every command was
        // refused and read as nothing: `config set` printed "Set … (v-1)" and wrote nothing.
        if (!await connection.RegisterAsync(CliAppId, "Pragmatic CLI").ConfigureAwait(false))
        {
            Console.Error.WriteLine($"The Agent at {socketPath} refused to register the CLI.");
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "status" => await StatusAsync(connection).ConfigureAwait(false),
                "config" => await ConfigAsync(connection, subArgs).ConfigureAwait(false),
                "flag" => await FlagAsync(connection, subArgs).ConfigureAwait(false),
                "maintenance" => await MaintenanceAsync(connection, subArgs).ConfigureAwait(false),
                _ => PrintUsage()
            };
        }
        catch (AgentRequestRefusedException refused)
        {
            Console.Error.WriteLine(refused.Message);
            return 1;
        }
    }

    /// <summary>The id the CLI registers under, for the time of one command; left out of <c>status</c>.</summary>
    private const string CliAppId = "pragmatic-cli";

    // The Gateway's full-gateway maintenance toggle (Pragmatic.Gateway GatewayKvSchema.FullMaintenanceKey).
    // The daemon can't reference Pragmatic.Gateway — the Gateway depends on the Agent, not the reverse —
    // so the contract key is mirrored here as a literal. MaintenanceState treats value "true" as active.
    // This verb is the key's only writer in shipped code.
    private const string GatewayMaintenanceKey = "state/gateway/maintenance";

    private static async Task<int> MaintenanceAsync(AgentConnection connection, string[] args)
    {
        if (args.Length == 0)
            return PrintMaintenanceUsage();

        return args[0].ToLowerInvariant() switch
        {
            "on" => await SetMaintenanceAsync(connection, true).ConfigureAwait(false),
            "off" => await SetMaintenanceAsync(connection, false).ConfigureAwait(false),
            "status" => await MaintenanceStatusAsync(connection).ConfigureAwait(false),
            _ => PrintMaintenanceUsage()
        };
    }

    private static async Task<int> SetMaintenanceAsync(AgentConnection connection, bool on)
    {
        var (version, conflict) = await connection
            .KvSetAsync(GatewayMaintenanceKey, on ? "true" : "false").ConfigureAwait(false);

        if (conflict)
        {
            Console.Error.WriteLine("CAS conflict — value was changed by another writer");
            return 1;
        }

        Console.WriteLine($"Full-gateway maintenance {(on ? "ENABLED" : "disabled")} (v{version})");
        return 0;
    }

    private static async Task<int> MaintenanceStatusAsync(AgentConnection connection)
    {
        var (value, _, found) = await connection.KvGetAsync(GatewayMaintenanceKey).ConfigureAwait(false);
        Console.WriteLine($"Full-gateway maintenance: {(found && value == "true" ? "ON" : "off")}");
        return 0;
    }

    private static int PrintMaintenanceUsage()
    {
        Console.Error.WriteLine("Usage: pragmatic maintenance <on|off|status>");
        return 1;
    }

    private static async Task<int> StatusAsync(AgentConnection connection)
    {
        // Without the CLI itself: it registered only to ask. One entry per running instance.
        var instances = (await connection.KvPrefixAsync(HostRosterKeys.Prefix).ConfigureAwait(false))
            .Where(instance => !instance.Key.StartsWith(HostRosterKeys.AppPrefix(CliAppId), StringComparison.Ordinal))
            .ToList();

        Console.WriteLine("Pragmatic Agent Status");
        Console.WriteLine("═════════════════════");
        Console.WriteLine($"Connected: true");
        Console.WriteLine($"Instances registered: {instances.Count}");

        foreach (var instance in instances)
        {
            var appAndInstance = instance.Key[HostRosterKeys.Prefix.Length..];
            Console.WriteLine($"  {appAndInstance}: {instance.Value ?? "unknown"}");
        }

        return 0;
    }

    private static async Task<int> ConfigAsync(AgentConnection connection, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: pragmatic config <set|get|list> [args]");
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "set" when args.Length >= 3 => await ConfigSetAsync(connection, args[1], args[2], GetTenantArg(args)).ConfigureAwait(false),
            "get" when args.Length >= 2 => await ConfigGetAsync(connection, args[1], GetTenantArg(args)).ConfigureAwait(false),
            "list" => await ConfigListAsync(connection, args.Length >= 2 ? args[1] : "config/").ConfigureAwait(false),
            _ => PrintConfigUsage()
        };
    }

    private static async Task<int> ConfigSetAsync(AgentConnection connection, string key, string value, string? tenant)
    {
        var kvKey = tenant is not null ? $"config/tenant:{tenant}/{key}" : $"config/{key}";
        var (version, conflict) = await connection.KvSetAsync(kvKey, value).ConfigureAwait(false);

        if (conflict)
        {
            Console.Error.WriteLine("CAS conflict — value was changed by another writer");
            return 1;
        }

        Console.WriteLine($"Set {kvKey} = {value} (v{version})");
        return 0;
    }

    private static async Task<int> ConfigGetAsync(AgentConnection connection, string key, string? tenant)
    {
        var kvKey = tenant is not null ? $"config/tenant:{tenant}/{key}" : $"config/{key}";
        var (value, version, found) = await connection.KvGetAsync(kvKey).ConfigureAwait(false);

        if (!found)
        {
            Console.Error.WriteLine($"Key not found: {kvKey}");
            return 1;
        }

        Console.WriteLine($"{kvKey} = {value} (v{version})");
        return 0;
    }

    private static async Task<int> ConfigListAsync(AgentConnection connection, string prefix)
    {
        if (!prefix.StartsWith("config/", StringComparison.Ordinal))
            prefix = $"config/{prefix}";

        var entries = await connection.KvPrefixAsync(prefix).ConfigureAwait(false);

        if (entries.Count == 0)
        {
            Console.WriteLine("No entries found.");
            return 0;
        }

        foreach (var entry in entries)
            Console.WriteLine($"  {entry.Key} = {entry.Value} (v{entry.Version})");

        return 0;
    }

    private static async Task<int> FlagAsync(AgentConnection connection, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: pragmatic flag <set|get|list> [args]");
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "set" when args.Length >= 3 => await FlagSetAsync(connection, args[1], args[2]).ConfigureAwait(false),
            "get" when args.Length >= 2 => await FlagGetAsync(connection, args[1]).ConfigureAwait(false),
            "list" => await FlagListAsync(connection).ConfigureAwait(false),
            _ => PrintFlagUsage()
        };
    }

    private static async Task<int> FlagSetAsync(AgentConnection connection, string name, string value)
    {
        var kvKey = $"flags/{name}";
        var (version, _) = await connection.KvSetAsync(kvKey, value).ConfigureAwait(false);
        Console.WriteLine($"Set flag {name} = {value} (v{version})");
        return 0;
    }

    private static async Task<int> FlagGetAsync(AgentConnection connection, string name)
    {
        var kvKey = $"flags/{name}";
        var (value, version, found) = await connection.KvGetAsync(kvKey).ConfigureAwait(false);

        if (!found)
        {
            Console.Error.WriteLine($"Flag not found: {name}");
            return 1;
        }

        Console.WriteLine($"{name} = {value} (v{version})");
        return 0;
    }

    private static async Task<int> FlagListAsync(AgentConnection connection)
    {
        var entries = await connection.KvPrefixAsync("flags/").ConfigureAwait(false);

        if (entries.Count == 0)
        {
            Console.WriteLine("No flags defined.");
            return 0;
        }

        foreach (var entry in entries)
        {
            var name = entry.Key["flags/".Length..];
            Console.WriteLine($"  {name} = {entry.Value}");
        }

        return 0;
    }

    private static string? GetTenantArg(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--tenant")
                return args[i + 1];
        }

        return null;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            Pragmatic Agent CLI

            Usage: pragmatic <command> [options]

            Commands:
              status                    Show agent and app status
              config set <key> <value>  Set a configuration value
              config get <key>          Get a configuration value
              config list [prefix]      List configuration entries
              flag set <name> <value>   Set a feature flag
              flag get <name>           Get a feature flag
              flag list                 List all feature flags
              maintenance <on|off>      Toggle full-gateway maintenance
              maintenance status        Show full-gateway maintenance state

            Options:
              --socket <path>           Agent socket path override
              --tenant <name>           Tenant scope for config operations
            """);
        return 1;
    }

    private static int PrintConfigUsage()
    {
        Console.Error.WriteLine("Usage: pragmatic config <set|get|list> [key] [value] [--tenant name]");
        return 1;
    }

    private static int PrintFlagUsage()
    {
        Console.Error.WriteLine("Usage: pragmatic flag <set|get|list> [name] [value]");
        return 1;
    }
}
