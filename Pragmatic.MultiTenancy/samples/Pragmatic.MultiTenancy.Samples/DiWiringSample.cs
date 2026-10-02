using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Pragmatic.Composition;
using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Resolvers;

namespace Pragmatic.MultiTenancy.Samples;

/// <summary>
///     Demonstrates <see cref="MultiTenancyServiceExtensions.AddPragmaticMultiTenancy"/> DI wiring,
///     the <see cref="MultiTenancyBuilder"/> <c>Use*</c> strategies (each one builds a real container
///     and the resolved <see cref="ITenantResolver"/> is inspected), and the
///     <c>UseMultiTenancy()</c> <see cref="IPragmaticBuilder"/> extension.
/// </summary>
public static class DiWiringSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. DI Wiring — AddPragmaticMultiTenancy + MultiTenancyBuilder");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // 4.1 Default registration (no configure) → single-tenant mode.
        Console.WriteLine("  4.1 AddPragmaticMultiTenancy() with no configure → single-tenant default");
        Console.WriteLine("  ----------------------------------------------------------------------");
        var defaultProvider = new ServiceCollection()
            .AddPragmaticMultiTenancy()
            .BuildServiceProvider();

        var defaultResolver = defaultProvider.GetRequiredService<ITenantResolver>();
        Console.WriteLine($"    Resolver registered : {defaultResolver.GetType().Name}");
        Console.WriteLine($"    ITenantContext       : {defaultProvider.GetRequiredService<ITenantContext>().GetType().Name}");
        Console.WriteLine();

        // 4.2 Each Use* strategy registers a different ITenantResolver.
        Console.WriteLine("  4.2 MultiTenancyBuilder strategies — each registers its resolver");
        Console.WriteLine("  ----------------------------------------------------------------");
        PrintStrategy("UseSingleTenant(\"acme\")", b => b.UseSingleTenant("acme"), expectsHttp: false);
        PrintStrategy("UseHeader()", b => b.UseHeader(), expectsHttp: true);
        PrintStrategy("UseClaim()", b => b.UseClaim(), expectsHttp: true);
        PrintStrategy("UseSubdomain()", b => b.UseSubdomain(), expectsHttp: true);
        PrintStrategy("UseRoute()", b => b.UseRoute(), expectsHttp: true);
        Console.WriteLine();

        // 4.3 Custom header name flows into MultiTenancyOptions.
        Console.WriteLine("  4.3 UseHeader(\"X-Org\") → custom header bound into MultiTenancyOptions");
        Console.WriteLine("  --------------------------------------------------------------------");
        var customHeaderProvider = new ServiceCollection()
            .AddHttpContextAccessor()
            .AddPragmaticMultiTenancy(b => b.UseHeader("X-Org"))
            .BuildServiceProvider();
        var headerName = customHeaderProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value.TenantHeaderName;
        Console.WriteLine($"    MultiTenancyOptions.TenantHeaderName = \"{headerName}\"");
        Console.WriteLine();

        // 4.4 UseMultiTenancy() on IPragmaticBuilder (3-tier strategy entry point).
        Console.WriteLine("  4.4 IPragmaticBuilder.UseMultiTenancy(...) — strategy tier wiring");
        Console.WriteLine("  ----------------------------------------------------------------");
        var pragmaticBuilder = new SamplePragmaticBuilder(new ServiceCollection());
        pragmaticBuilder.UseMultiTenancy(b => b.UseSubdomain());
        pragmaticBuilder.Services.AddOptions<MultiTenancyOptions>(); // subdomain resolver needs no options, but header/claim/route would
        var builderProvider = pragmaticBuilder.Services
            .AddHttpContextAccessor()
            .BuildServiceProvider();
        Console.WriteLine($"    Resolver via IPragmaticBuilder : {builderProvider.GetRequiredService<ITenantResolver>().GetType().Name}");

        // Convenience overload with no configure → single-tenant default.
        var defaultBuilder = new SamplePragmaticBuilder(new ServiceCollection());
        defaultBuilder.UseMultiTenancy();
        var defaultBuilderProvider = defaultBuilder.Services.BuildServiceProvider();
        Console.WriteLine($"    UseMultiTenancy() (no args)    : {defaultBuilderProvider.GetRequiredService<ITenantResolver>().GetType().Name}");
        Console.WriteLine();
    }

    private static void PrintStrategy(string label, Action<MultiTenancyBuilder> configure, bool expectsHttp)
    {
        var services = new ServiceCollection();
        if (expectsHttp)
        {
            services.AddHttpContextAccessor();
            services.AddOptions<MultiTenancyOptions>(); // HTTP resolvers depend on IOptions<MultiTenancyOptions>
        }

        using var provider = services
            .AddPragmaticMultiTenancy(configure)
            .BuildServiceProvider();

        var resolver = provider.GetRequiredService<ITenantResolver>();
        Console.WriteLine($"    {label,-24} → {resolver.GetType().Name}");
    }

    /// <summary>
    ///     Minimal <see cref="IPragmaticBuilder"/> for the sample. In a real app the host supplies
    ///     this (via <c>PragmaticApp.RunAsync</c>); here we stub Configuration/Environment with empties
    ///     since the multi-tenancy <c>Use*</c> methods only touch <see cref="Services"/>.
    /// </summary>
    private sealed class SamplePragmaticBuilder(IServiceCollection services) : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = services;
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new SampleHostEnvironment();
    }

    private sealed class SampleHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Pragmatic.MultiTenancy.Samples";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
