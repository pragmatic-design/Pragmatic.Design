using Pragmatic.Composition;

namespace Pragmatic.Composition.Samples.Samples;

/// <summary>
///     Compile-time module / host topology attributes consumed by the source generator:
///     <c>[IncludeModule&lt;T&gt;]</c> (library-level deps), <c>[Include&lt;...&gt;]</c> (host
///     module→database wiring), <c>[NeedsStep&lt;T&gt;]</c>, <c>[RequiresConfig]</c>, and
///     <c>[UsePackage&lt;T&gt;]</c>/<see cref="IPackageDefinition" />.
///     These shape the generated host — the usage is shown as source; the package contract is
///     exercised live via its static-abstract members.
/// </summary>
public static class ModuleCompositionSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Module Composition — [IncludeModule]/[Include]/[NeedsStep]/[UsePackage]");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  Library-level dependency: [IncludeModule<TModule>] on a [Module]:");
        Console.WriteLine("  ──────────────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [Module(Name = "Booking")]
            [IncludeModule<IdentityModule>]      // type-safe dependency (replaces [DependsOn])
            [IncludeModule<NotificationsModule>]
            public class BookingModule;
        """);
        Console.WriteLine();

        Console.WriteLine("  Host module→database wiring: [Include<TModule[,TDatabase[,TDbContext]]>]:");
        Console.WriteLine("  ─────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [PragmaticHost]
            [Include<BookingModule>]                          // no DB — no DbContext generated
            [Include<BillingModule, AppDatabase>]             // DbContext auto-named BillingDbContext
            [Include<AuditModule, AppDatabase, AuditDbContext>] // explicit DbContext class name
            public partial class Host;
        """);
        Console.WriteLine();

        Console.WriteLine("  Pipeline requirement: [NeedsStep<TStep>] on a [Module]:");
        Console.WriteLine("  ─────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [Module(Name = "Booking")]
            [NeedsStep<TenantResolutionStep>]   // SG aggregates, dedups, orders by IStartupStep.Order
            public class BookingModule;
        """);
        Console.WriteLine();

        Console.WriteLine("  Fail-fast config: [RequiresConfig] (drives generated ValidateConfiguration()):");
        Console.WriteLine("  ────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [StartupStep]
            [RequiresConfig("ConnectionStrings:App", Description = "Primary database")]
            [RequiresConfig("Stripe:ApiKey")]
            public class BillingStartupStep : IStartupStep { ... }
            // Missing keys => InvalidOperationException at startup, before app.RunAsync().
        """);
        Console.WriteLine();

        Console.WriteLine("  Package import: [UsePackage<TPackage>] + IPackageDefinition:");
        Console.WriteLine("  ──────────────────────────────────────────────────────────────");
        Console.WriteLine("""
            [Boundary]
            [UsePackage<LocalIdentityPackage>(RoutePrefix = "auth/local")]  // override default prefix
            public class AccountsBoundary;
        """);
        Console.WriteLine();

        // IPackageDefinition uses static-abstract members — exercise the demo package live.
        Console.WriteLine("  Live IPackageDefinition metadata (read via static-abstract members):");
        Console.WriteLine($"    PackageName       = {LocalIdentityPackage.PackageName}");
        Console.WriteLine($"    RoutePrefix       = {LocalIdentityPackage.RoutePrefix}");
        Console.WriteLine($"    Description       = {LocalIdentityPackage.Description}");
        PrintPackageMetadata<LocalIdentityPackage>();
        Console.WriteLine();
    }

    // Generic helper showing how the SG reads any package's metadata uniformly via the interface.
    private static void PrintPackageMetadata<TPackage>() where TPackage : IPackageDefinition
    {
        Console.WriteLine($"    (resolved generically via IPackageDefinition: {TPackage.PackageName})");
    }
}

/// <summary>
///     A minimal <see cref="IPackageDefinition" /> demonstrating the static-abstract contract
///     the SG reads when fusing a package into a host module via <c>[UsePackage&lt;T&gt;]</c>.
/// </summary>
public sealed class LocalIdentityPackage : IPackageDefinition
{
    /// <inheritdoc />
    public static string PackageName => "Identity.Local";

    /// <inheritdoc />
    public static string? RoutePrefix => "identity/local";

    /// <inheritdoc />
    public static string? Description => "Username/password local identity provider.";
}
