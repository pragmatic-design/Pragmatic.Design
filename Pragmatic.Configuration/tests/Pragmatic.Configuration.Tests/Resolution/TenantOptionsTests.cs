using System.Collections.Generic;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Configuration;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;
using Xunit;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     Verifies #5: <see cref="ITenantOptions{T}"/> binds a POCO for the current tenant, layering the per-tenant
///     store cascade over the appsettings fallback.
/// </summary>
public class TenantOptionsTests
{
    private sealed class BillingOptions
    {
        public decimal TaxRate { get; set; }
        public string Currency { get; set; } = "";
    }

    private sealed class FakeTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantId;
        public bool IsResolved => tenantId is not null;
    }

    private static IConfiguration AppSettings(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in pairs)
            dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public async Task GetAsync_TenantOverride_WinsOverBase_WithAppsettingsFallback()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Billing:TaxRate", "0.10");                  // store base
        await store.SetAsync("Billing:TaxRate", "0.22", "tenant-it");     // tenant override
        var resolver = new ConfigurationResolver(
            store, EnvironmentProfile.From("Production"), new FakeTenantContext("tenant-it"));

        var appsettings = AppSettings(("Billing:Currency", "EUR"));        // fallback-only key
        var sut = new TenantOptions<BillingOptions>(resolver, appsettings);

        var options = await sut.GetAsync();

        options.TaxRate.Should().Be(0.22m);    // tenant store value wins
        options.Currency.Should().Be("EUR");   // appsettings fallback for an unset store key
    }

    [Fact]
    public async Task GetAsync_NoTenantResolved_UsesStoreBaseOverAppsettings()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Billing:TaxRate", "0.10");                  // store base only
        var resolver = new ConfigurationResolver(
            store, EnvironmentProfile.From("Production"), new FakeTenantContext(null));

        var appsettings = AppSettings(("Billing:TaxRate", "0.05"), ("Billing:Currency", "USD"));
        var sut = new TenantOptions<BillingOptions>(resolver, appsettings);

        var options = await sut.GetAsync();

        options.TaxRate.Should().Be(0.10m);    // store base overrides appsettings
        options.Currency.Should().Be("USD");   // appsettings (no store value)
    }
}
