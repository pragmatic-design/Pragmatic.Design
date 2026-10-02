using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Boundary;
using Pragmatic.Actions.Samples.Boundaries;

namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     Boundary definitions and runtime configuration.
///     Demonstrates real, executed: <c>IBoundary</c> markers, <c>AddBoundary&lt;T&gt;</c>,
///     <c>UseLocal()</c> / <c>UseRemote(url)</c>, and the refusal of a second registration.
/// </summary>
/// <remarks>
///     Unlike the catalog samples, this one builds a real <see cref="IServiceCollection" />
///     and invokes the boundary APIs so the printed output reflects actual runtime state.
/// </remarks>
public static class BoundarySample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Boundaries — Definitions, Local/Remote, One Registration Each");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowLocalBoundary();
        ShowRemoteBoundary();
        ShowTopologyValidation();

        Console.WriteLine();
    }

    private static void ShowLocalBoundary()
    {
        Console.WriteLine("  6.1 Local boundary — UseLocal()");
        Console.WriteLine("  -----------------------------------");

        // Build a real configuration object and inspect its runtime state.
        var config = new BoundaryConfiguration<OrdersBoundary>().UseLocal();

        Console.WriteLine($"    BoundaryType : {config.BoundaryType.Name}");
        Console.WriteLine($"    Mode         : {config.Mode}");
        Console.WriteLine($"    RemoteBaseUrl: {config.RemoteBaseUrl ?? "(none — in-process)"}");
        Console.WriteLine();
        Console.WriteLine("    // Local boundaries run in-process. In a real app you would chain");
        Console.WriteLine("    // .UseDatabase(...) from Pragmatic.Actions.EFCore to wire a DbContext.");
        Console.WriteLine();
    }

    private static void ShowRemoteBoundary()
    {
        Console.WriteLine("  6.2 Remote boundary — UseRemote(baseUrl)");
        Console.WriteLine("  --------------------------------------------");

        var config = new BoundaryConfiguration<ShippingBoundary>()
            .UseRemote("https://shipping-api.example.com/");

        Console.WriteLine($"    BoundaryType : {config.BoundaryType.Name}");
        Console.WriteLine($"    Mode         : {config.Mode}");
        Console.WriteLine($"    RemoteBaseUrl: {config.RemoteBaseUrl}  (trailing slash trimmed)");
        Console.WriteLine();

        // Register on a real service collection — this also registers a named HttpClient
        // "Pragmatic.Remote.Shipping" used by SG-generated HTTP invokers.
        var services = new ServiceCollection();
        services.AddBoundary<ShippingBoundary>(cfg => cfg.UseRemote("https://shipping-api.example.com"));

        var registered = services.HasBoundary<ShippingBoundary>();
        Console.WriteLine($"    services.HasBoundary<ShippingBoundary>() = {registered}");
        Console.WriteLine("    // AddBoundary also registered HttpClient \"Pragmatic.Remote.Shipping\".");
        Console.WriteLine();
    }

    private static void ShowTopologyValidation()
    {
        Console.WriteLine("  6.3 A boundary is registered once — the topology is checked at build time");
        Console.WriteLine("  -----------------------------------------------------------------------------");

        var services = new ServiceCollection();
        services.AddBoundary<OrdersBoundary>(cfg => cfg.UseRemote("https://orders-api.example.com"));

        try
        {
            // A second registration of the same boundary is a mistake AddBoundary refuses on the spot:
            // accepted, it would leave the first configuration in force and ignore this one.
            services.AddBoundary<OrdersBoundary>(cfg => cfg.UseRemote("https://orders-v2.example.com"));
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"    Refused: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("    // Dependencies between modules are not checked here: they are declared with");
        Console.WriteLine("    // [IncludeModule<TModule>], and the host's build fails on one that names no");
        Console.WriteLine("    // module (PRAG1601) or on a cycle (PRAG1602) — before anything runs.");
        Console.WriteLine();
    }
}
