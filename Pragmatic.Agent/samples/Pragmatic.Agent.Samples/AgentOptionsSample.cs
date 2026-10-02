using Pragmatic.Agent.Client;

namespace Pragmatic.Agent.Samples;

// ─────────────────────────────────────────────────────────────────────────────
// Sample 4 — AgentOptions client configuration.
//
// AgentOptions is what a Pragmatic host configures when it calls UseAgent(): the
// socket/named-pipe path, the app identity, the heartbeat cadence and whether to
// gracefully degrade when the daemon is absent. This sample shows the
// OS-sensitive defaults and a customized instance. (It only constructs the
// options object — it does NOT open a connection, which would require a running
// daemon.)
// ─────────────────────────────────────────────────────────────────────────────

internal static class AgentOptionsSample
{
    public static void Run()
    {
        Console.WriteLine("== Sample 4: AgentOptions (client configuration) ==");
        Console.WriteLine();

        // Defaults — SocketPath is OS-sensitive (named pipe on Windows, socket elsewhere).
        var defaults = new AgentOptions();
        Console.WriteLine("  Defaults:");
        Console.WriteLine($"      SocketPath        = '{defaults.SocketPath}'");
        Console.WriteLine($"      AppId / AppName   = {Show(defaults.AppId)} / {Show(defaults.AppName)}  (default: assembly name)");
        Console.WriteLine($"      HeartbeatInterval = {defaults.HeartbeatInterval.TotalSeconds:0}s");
        Console.WriteLine($"      AutoReconnect     = {defaults.AutoReconnect}  (graceful degradation when the daemon is down)");
        Console.WriteLine();

        // A customized host configuration.
        var custom = new AgentOptions
        {
            AppId = "booking-service",
            AppName = "Booking Service",
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            AutoReconnect = false, // fail fast if the daemon is unavailable
        };
        Console.WriteLine("  Customized (booking-service):");
        Console.WriteLine($"      AppId='{custom.AppId}', AppName='{custom.AppName}'");
        Console.WriteLine($"      HeartbeatInterval={custom.HeartbeatInterval.TotalSeconds:0}s, AutoReconnect={custom.AutoReconnect}");
        Console.WriteLine();
    }

    private static string Show(string? value) => value is null ? "(null)" : $"'{value}'";
}
