namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     What the SG generates for the host: services, databases, topology.
/// </summary>
public static class SgOutputSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. SG Output — Generated Host Infrastructure");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  Host.Entry.g.cs — PragmaticApp entry point");
        Console.WriteLine("  ─────────────────────────────────────────────");
        Console.WriteLine("    Generates PragmaticApp.RunAsync/RunWorkerAsync");
        Console.WriteLine("    Orchestrates: config validation → DI → build → pipeline → run");
        Console.WriteLine();

        Console.WriteLine("  Host.Services.g.cs — aggregate service registration");
        Console.WriteLine("  ──────────────────────────────────────────────────────");
        Console.WriteLine("    RegisterAllDomainActions()     — action invokers");
        Console.WriteLine("    RegisterAllRepositories()      — entity repositories");
        Console.WriteLine("    RegisterAllDatabases()         — DbContext instances");
        Console.WriteLine("    RegisterAllPersistenceFilters() — soft delete, ownership, tenant");
        Console.WriteLine("    RegisterAllEventHandlers()      — domain event subscriptions");
        Console.WriteLine("    RegisterAllEndpointProcessors() — pre/post processors");
        Console.WriteLine("    RegisterAllDecorators()         — DI decorator chains");
        Console.WriteLine("    CallConfigureServices()         — invoke [StartupStep] in order");
        Console.WriteLine("    ConfigurePipeline()             — invoke ConfigurePipeline in order");
        Console.WriteLine("    MapAllEndpoints()               — route all [Endpoint] handlers");
        Console.WriteLine();

        Console.WriteLine("  Auto-registered modules (when NuGet is referenced):");
        Console.WriteLine("  ─────────────────────────────────────────────────────────");
        Console.WriteLine("    Temporal       → SystemClock as IClock (Singleton)");
        Console.WriteLine("    Resilience     → AddPragmaticResilience()");
        Console.WriteLine("    Caching        → AddHybridCache() + AddPragmaticCaching()");
        Console.WriteLine("    I18n           → AddPragmaticI18n()");
        Console.WriteLine("    Identity       → AddPragmaticAuthorization()");
        Console.WriteLine("    MultiTenancy   → AddPragmaticMultiTenancy() (SingleTenant default)");
        Console.WriteLine("    FeatureFlags   → AddPragmaticFeatureFlags()");
        Console.WriteLine("    Discovery      → AddPragmaticDiscovery()");
        Console.WriteLine();
        Console.WriteLine("    Add NuGet → SG detects → generates registration.");
        Console.WriteLine("    Remove NuGet → registration disappears. Zero configuration.");
        Console.WriteLine();
    }
}
