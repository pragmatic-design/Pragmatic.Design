using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Benchmarks.Workload;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Configuration.Benchmarks;

/// <summary>
///     A tenant's options read once per request, as an action reads them: <c>ITenantOptions&lt;T&gt;.GetAsync</c>
///     through the generated binder and through the reflective one, against <c>IOptionsSnapshot&lt;T&gt;</c> and
///     <c>IOptionsMonitor&lt;T&gt;</c>, for a class of ten scalar properties (#139).
/// </summary>
/// <remarks>
///     <para>
///         The host is composed as an application's is: <c>AddPragmaticConfiguration</c> with the tenant layer on
///         and read caching on (its default), so the store reads go through <c>CachingConfigurationStore</c>, and
///         the generated <c>AddBillingOptions</c>, which registers the typed binder. Three keys are the tenant's in
///         the store; the other seven come from appsettings.
///     </para>
///     <para>
///         Each per-request row opens a scope, as a request has one: <c>IOptionsSnapshot</c> is computed once per
///         scope, and <c>ITenantOptions</c> is scoped. <c>IOptionsMonitor.CurrentValue</c> is a singleton's cached
///         value and opens none. The snapshot and the monitor do not see the tenant's store values at all; they
///         are the cost of an ordinary options read, which is what the question "is a cache needed" is measured
///         against.
///     </para>
/// </remarks>
[MemoryDiagnoser]
public class TenantOptionsBenchmarks
{
    private ServiceProvider _provider = null!;
    private IOptionsMonitor<BillingOptions> _monitor = null!;

    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    [Benchmark(Baseline = true)]
    public BillingOptions OptionsSnapshot()
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<BillingOptions>>().Value;
    }

    [Benchmark]
    public BillingOptions OptionsMonitor() => _monitor.CurrentValue;

    [Benchmark]
    public async Task<BillingOptions> TenantOptions_GeneratedBinder()
    {
        var scope = _provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            return await scope.ServiceProvider.GetRequiredService<ITenantOptions<BillingOptions>>()
                .GetAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<BillingOptions> TenantOptions_Reflection()
    {
        var scope = _provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            return await Reflective(scope.ServiceProvider).GetAsync().ConfigureAwait(false);
    }

    public async Task SetupAsync()
    {
        var appsettings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Currency"] = "EUR",
                ["Billing:TaxRate"] = "0.20",
                ["Billing:PaymentTermDays"] = "30",
                ["Billing:SendReminders"] = "true",
                ["Billing:ReminderEveryDays"] = "7",
                ["Billing:InvoicePrefix"] = "INV",
                ["Billing:LateFeeRate"] = "0.01",
                ["Billing:MaxLines"] = "500",
                ["Billing:Region"] = "eu-south",
                ["Billing:RoundToCents"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(appsettings);
        services.AddScoped<ITenantContext, ResolvedTenant>();
        // Named in full: the generator's aggregator for this assembly is also AddPragmaticConfiguration, in the
        // workload's namespace, and the compiler would pick it.
        Extensions.ConfigurationServiceCollectionExtensions.AddPragmaticConfiguration(
            services, options => options.MultiTenant.Enabled = true);
        BillingOptionsConfigurationExtensions.AddBillingOptions(services, appsettings);
        _provider = services.BuildServiceProvider();

        var store = _provider.GetRequiredService<IConfigurationStore>();
        await store.SetAsync("Billing:TaxRate", "0.22", ResolvedTenant.Id).ConfigureAwait(false);
        await store.SetAsync("Billing:Currency", "CHF", ResolvedTenant.Id).ConfigureAwait(false);
        await store.SetAsync("Billing:MaxLines", "1000", ResolvedTenant.Id).ConfigureAwait(false);

        _monitor = _provider.GetRequiredService<IOptionsMonitor<BillingOptions>>();
    }

    public async Task CleanupAsync() => await _provider.DisposeAsync().ConfigureAwait(false);

    /// <summary>
    ///     Reads each row once and fails unless the tenant rows see the tenant's values over appsettings', the
    ///     ordinary rows appsettings' alone, and the container injected the generated binder.
    /// </summary>
    public async Task VerifyAsync()
    {
        var typed = await TenantOptions_GeneratedBinder().ConfigureAwait(false);
        var reflected = await TenantOptions_Reflection().ConfigureAwait(false);
        var snapshot = OptionsSnapshot();
        var monitor = OptionsMonitor();

        var failures = new List<string>();
        if (_provider.GetService<ITenantOptionsBinder<BillingOptions>>() is null)
            failures.Add("no generated ITenantOptionsBinder<BillingOptions> is registered");
        foreach (var (name, options) in new[] { ("generated binder", typed), ("reflection", reflected) })
        {
            if (options is not { TaxRate: 0.22m, Currency: "CHF", MaxLines: 1000, Region: "eu-south", PaymentTermDays: 30 })
                failures.Add($"the {name} row did not read the tenant's values over appsettings'");
        }

        foreach (var (name, options) in new[] { ("snapshot", snapshot), ("monitor", monitor) })
        {
            if (options is not { TaxRate: 0.20m, Currency: "EUR", MaxLines: 500, Region: "eu-south" })
                failures.Add($"the {name} row did not read appsettings");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("The rows would measure something else: " + string.Join("; ", failures));

        Console.WriteLine("Generated binder and reflection read the tenant's values; snapshot and monitor read appsettings.");
    }

    // The same service the container builds, without the binder: what TenantOptions did before #193, and what it
    // still does for a class the generator cannot bind whole.
    private static TenantOptions<BillingOptions> Reflective(IServiceProvider services) => new(
        services.GetRequiredService<IConfigurationResolver>(),
        services.GetRequiredService<IConfiguration>());
}
