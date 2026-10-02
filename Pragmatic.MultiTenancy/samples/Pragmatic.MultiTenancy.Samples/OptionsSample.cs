using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.MultiTenancy;

namespace Pragmatic.MultiTenancy.Samples;

/// <summary>
///     Shows <see cref="MultiTenancyOptions"/>: its defaults, configuration via
///     <c>services.Configure</c>, and resolution through <see cref="IOptions{T}"/> from DI.
/// </summary>
public static class OptionsSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("9. MultiTenancyOptions — defaults & configuration (executed)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // 9.1 Defaults.
        Console.WriteLine("  9.1 Default values");
        Console.WriteLine("  ------------------");
        var defaults = new MultiTenancyOptions();
        Console.WriteLine($"    DefaultTenantId      : \"{defaults.DefaultTenantId}\"");
        Console.WriteLine($"    TenantHeaderName     : \"{defaults.TenantHeaderName}\"");
        Console.WriteLine($"    TenantClaimType      : \"{defaults.TenantClaimType}\"");
        Console.WriteLine($"    TenantRouteParameter : \"{defaults.TenantRouteParameter}\"");
        Console.WriteLine($"    RequireTenant        : {defaults.RequireTenant}");
        Console.WriteLine();

        // 9.2 Configure via DI and resolve through IOptions<T>.
        Console.WriteLine("  9.2 services.Configure<MultiTenancyOptions>(...) → IOptions<T>");
        Console.WriteLine("  -------------------------------------------------------------");
        using var provider = new ServiceCollection()
            .Configure<MultiTenancyOptions>(o =>
            {
                o.DefaultTenantId = "primary";
                o.TenantHeaderName = "X-Org-Id";
                o.RequireTenant = true;
            })
            .BuildServiceProvider();

        var configured = provider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;
        Console.WriteLine($"    DefaultTenantId      : \"{configured.DefaultTenantId}\"");
        Console.WriteLine($"    TenantHeaderName     : \"{configured.TenantHeaderName}\"");
        Console.WriteLine($"    RequireTenant        : {configured.RequireTenant}");
        Console.WriteLine();
    }
}
