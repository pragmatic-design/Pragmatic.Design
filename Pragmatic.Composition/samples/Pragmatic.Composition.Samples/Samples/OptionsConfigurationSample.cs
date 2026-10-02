using Pragmatic.Composition.Hosting;

namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     Cross-cutting host configuration: <see cref="PragmaticOptions" /> and
///     <see cref="MaintenanceModeOptions" /> (set via the <c>IPragmaticBuilder</c> callback),
///     plus the fail-fast configuration validation behavior driven by <c>[RequiresConfig]</c>.
///     The options objects are populated live; the validation behavior is reproduced runnably.
/// </summary>
public static class OptionsConfigurationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Options & Configuration Validation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // PragmaticOptions / MaintenanceModeOptions are real, public, settable objects.
        var options = new PragmaticOptions
        {
            EnsureDatabaseCreated = true,   // dev convenience; mutually exclusive with AutoMigrations
            DetailedErrors = true,
        };
        options.MaintenanceMode.EnableRuntimeMaintenance = true;
        options.MaintenanceMode.AdminPath = "/admin/maintenance";
        options.MaintenanceMode.AdminApiKey = "dev-only-key";
        options.MaintenanceMode.IncludeStackTrace = true;

        Console.WriteLine("  PragmaticOptions (set inside the IPragmaticBuilder callback):");
        Console.WriteLine("  ───────────────────────────────────────────────────────────────");
        Console.WriteLine($"    AutoMigrations         = {options.AutoMigrations}");
        Console.WriteLine($"    EnsureDatabaseCreated  = {options.EnsureDatabaseCreated}");
        Console.WriteLine($"    DetailedErrors         = {options.DetailedErrors}");
        Console.WriteLine($"    Telemetry (present)    = {options.Telemetry is not null}");
        Console.WriteLine();
        Console.WriteLine("    Note: AutoMigrations and EnsureDatabaseCreated are mutually exclusive.");
        Console.WriteLine($"    Both true? {options is { AutoMigrations: true, EnsureDatabaseCreated: true }} (guarded at startup).");
        Console.WriteLine();

        Console.WriteLine("  MaintenanceModeOptions:");
        Console.WriteLine("  ────────────────────────");
        var mm = options.MaintenanceMode;
        Console.WriteLine($"    EnableRuntimeMaintenance = {mm.EnableRuntimeMaintenance}");
        Console.WriteLine($"    EnableOnStartupFailure   = {mm.EnableOnStartupFailure}");
        Console.WriteLine($"    HealthPath               = {mm.HealthPath}");
        Console.WriteLine($"    MaintenancePath          = {mm.MaintenancePath}");
        Console.WriteLine($"    AdminPath                = {mm.AdminPath}");
        Console.WriteLine($"    AdminApiKey set?         = {mm.AdminApiKey is not null} (null => localhost-only)");
        Console.WriteLine($"    IncludeStackTrace        = {mm.IncludeStackTrace}");
        Console.WriteLine($"    IncludeDetailedProgress  = {mm.IncludeDetailedProgress}");
        Console.WriteLine();

        Console.WriteLine("  Wired via the strategy callback:");
        Console.WriteLine("  ───────────────────────────────────");
        Console.WriteLine("""
            await PragmaticApp.RunAsync(args, app =>
            {
                app.UseDatabaseEnsureCreated();   // sets Options.EnsureDatabaseCreated
                app.UseMaintenanceMode(m =>        // sets Options.MaintenanceMode.*
                {
                    m.EnableRuntimeMaintenance = true;
                    m.AdminApiKey = config["Maintenance:ApiKey"];
                });
            });
        """);
        Console.WriteLine();

        // ConfigurationValidator behavior: [RequiresConfig] keys are collected by the SG and
        // validated before startup. The validator is internal; its contract is: missing keys throw.
        Console.WriteLine("  ConfigurationValidator behavior (fail-fast on missing keys):");
        Console.WriteLine("  ──────────────────────────────────────────────────────────────");
        var present = new Dictionary<string, string?>
        {
            ["ConnectionStrings:App"] = "Server=.;Database=App",
        };
        var required = new[] { "ConnectionStrings:App", "Stripe:ApiKey" };

        // Reproduce the validator's rule (a key is "present" iff non-null/non-empty).
        var missing = required
            .Where(key => string.IsNullOrEmpty(present.GetValueOrDefault(key)))
            .ToList();

        Console.WriteLine($"    Required keys: [{string.Join(", ", required)}]");
        Console.WriteLine($"    Present keys:  [{string.Join(", ", present.Keys)}]");
        if (missing.Count > 0)
            Console.WriteLine(
                $"    => Startup throws InvalidOperationException: missing [{string.Join(", ", missing)}]");
        else
            Console.WriteLine("    => All required keys present; startup proceeds.");
        Console.WriteLine();
        Console.WriteLine("    Declared with [RequiresConfig(\"Stripe:ApiKey\")] on a [StartupStep];");
        Console.WriteLine("    the SG emits ValidateConfiguration() invoked before app.RunAsync().");
        Console.WriteLine();
    }
}
