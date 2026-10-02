namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     The 3-tier composition model: Topology (SG) → Strategy (IPragmaticBuilder) → Business (IStartupStep).
/// </summary>
public static class ThreeTierModelSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. Three-Tier Composition Model");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  Tier 1: Topology (compile-time, automatic)");
        Console.WriteLine("  ──────────────────────────────────────────────");
        Console.WriteLine("    SG auto-detects referenced modules via FeatureDetector.");
        Console.WriteLine("    [Module], [Include<>], [BelongsTo<>] define the structure.");
        Console.WriteLine("    No manual registration — add a NuGet → SG sees it → generates.");
        Console.WriteLine();

        Console.WriteLine("  Tier 2: Module Strategy (Program.cs, explicit)");
        Console.WriteLine("  ──────────────────────────────────────────────────");
        Console.WriteLine("""
            await PragmaticApp.RunAsync(args, app =>
            {
                // Infrastructure choices — IPragmaticBuilder Use*() methods
                app.UseDatabaseEnsureCreated();  // or UseDatabaseMigrate()
                app.UseMultiTenancy(mt => mt.UseHeader());
                app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
                app.UseAuthorization(authz => { ... });
                app.UseLogging(log => { ... });
                app.UseStorage(sp => new LocalDiskFileStorage(...));
            });
        """);
        Console.WriteLine();

        Console.WriteLine("  Tier 3: Business Wiring (IStartupStep, per-module)");
        Console.WriteLine("  ──────────────────────────────────────────────────────");
        Console.WriteLine("""
            [StartupStep]
            public class MyStartupStep : IStartupStep
            {
                public int Order => 60;  // After routing (50)

                public void ConfigureServices(IServiceCollection services, ...)
                {
                    services.AddScoped<IOrderService, OrderService>();
                    services.AddScoped<IQueryFilter<Order>, TenantOrderFilter>();
                }

                public void ConfigurePipeline(IApplicationBuilder app)
                {
                    app.UseAuthentication();
                    app.UseAuthorization();
                }
            }
        """);
        Console.WriteLine();

        Console.WriteLine("  Execution order:");
        Console.WriteLine("  ─────────────────");
        Console.WriteLine("    1. SG registers defaults (Clock, Resilience, Caching, I18n, ...)");
        Console.WriteLine("    2. IPragmaticBuilder callback overrides (last-registration-wins)");
        Console.WriteLine("    3. IStartupStep.ConfigureServices() in Order sequence");
        Console.WriteLine("    4. app.Build()");
        Console.WriteLine("    5. IStartupStep.ConfigurePipeline() in Order sequence");
        Console.WriteLine("    6. Map all [Endpoint] routes");
        Console.WriteLine("    7. app.RunAsync()");
        Console.WriteLine();
    }
}
