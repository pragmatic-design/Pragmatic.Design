using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Bridge;
using Pragmatic.Configuration.Providers;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Bridge into Microsoft.Extensions.Configuration via
///     <see cref="ConfigurationBridgeExtensions.AddPragmaticStore"/>: the Pragmatic store becomes a
///     standard <see cref="IConfiguration"/> source, so <see cref="IConfiguration"/> indexers,
///     <c>GetSection</c> binding, and <see cref="IOptionsMonitor{T}"/> hot-reload all work against it.
/// </summary>
public static class BridgeSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Bridge — AddPragmaticStore() + IConfiguration + IOptionsMonitor");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Seed a store with values the bridge will expose to IConfiguration.
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Smtp:Host", "mail.example.com");
        await store.SetAsync("Smtp:Port", "587");

        var environment = EnvironmentProfile.From("Production");

        // ── Add the store as a configuration source ─────────────────────────────
        var configuration = new ConfigurationBuilder()
            .AddPragmaticStore(store, environment)
            .Build();

        Console.WriteLine("  Reading via IConfiguration (after bridge):");
        Console.WriteLine($"    config[\"Smtp:Host\"] = {configuration["Smtp:Host"]}");
        Console.WriteLine($"    config[\"Smtp:Port\"] = {configuration["Smtp:Port"]}");
        Console.WriteLine();

        // ── Bind a section to a strongly-typed options object via DI ────────────
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<SmtpSettings>(configuration.GetSection("Smtp"));
        using var sp = services.BuildServiceProvider();

        var monitor = sp.GetRequiredService<IOptionsMonitor<SmtpSettings>>();
        Console.WriteLine("  Bound SmtpSettings via IOptionsMonitor<T>:");
        Console.WriteLine($"    Host={monitor.CurrentValue.Host}, Port={monitor.CurrentValue.Port}");
        Console.WriteLine();

        // ── Hot-reload: a store change flows through to IOptionsMonitor ─────────
        var reloaded = new TaskCompletionSource();
        using var subscription = monitor.OnChange(updated =>
        {
            Console.WriteLine($"    [OnChange] SmtpSettings reloaded: Host={updated.Host}");
            reloaded.TrySetResult();
        });

        Console.WriteLine("  Changing Smtp:Host in the store...");
        await store.SetAsync("Smtp:Host", "new-mail.example.com");

        // The provider watches the store and calls IConfiguration.Reload(); options re-bind.
        var completed = await Task.WhenAny(reloaded.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        if (completed == reloaded.Task)
            Console.WriteLine($"    Current value now: Host={monitor.CurrentValue.Host}");
        else
            Console.WriteLine("    (reload callback did not fire within the demo window)");

        Console.WriteLine();
    }

    private sealed class SmtpSettings
    {
        public string Host { get; set; } = "";
        public int Port { get; set; }
    }
}
