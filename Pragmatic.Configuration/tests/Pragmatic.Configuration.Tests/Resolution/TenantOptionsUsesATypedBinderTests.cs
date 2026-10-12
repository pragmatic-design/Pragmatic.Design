using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     With a typed binder registered for the class, <see cref="TenantOptions{T}" /> binds through it, reading
///     each key from the tenant's store overlay before appsettings (#139).
/// </summary>
/// <remarks>
///     Without one it builds a <c>ConfigurationBuilder</c> and runs the reflective <c>Bind</c> on every call. The
///     binder here is written by hand, so this pins what <c>TenantOptions</c> hands any binder; what the generator
///     emits is checked against the reflective binder in <c>TheGeneratedTenantBinderBindsAsTheConfigurationBinderTests</c>.
/// </remarks>
public class TenantOptionsUsesATypedBinderTests
{
    private sealed class BillingOptions
    {
        public string TaxRate { get; set; } = "";
        public string Currency { get; set; } = "";
        public string Region { get; set; } = "default";
    }

    private sealed class RecordingBinder : ITenantOptionsBinder<BillingOptions>
    {
        public int Calls { get; private set; }

        public void Bind(BillingOptions target, Func<string, string?> value)
        {
            Calls++;
            if (value("TaxRate") is { } tax) target.TaxRate = tax;
            if (value("currency") is { } currency) target.Currency = currency;   // keys are case-insensitive
            if (value("Region") is { } region) target.Region = region;
        }
    }

    private sealed class FakeTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantId;
        public bool IsResolved => tenantId is not null;
    }

    [Fact]
    public async Task GetAsync_WithABinder_BindsThroughItTenantFirstThenAppsettings()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Billing:TaxRate", "0.10");
        await store.SetAsync("Billing:TaxRate", "0.22", "tenant-it");
        var resolver = new ConfigurationResolver(store, EnvironmentProfile.From("Production"), new FakeTenantContext("tenant-it"));
        var appsettings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:Currency"] = "EUR" })
            .Build();
        var binder = new RecordingBinder();

        var options = await new TenantOptions<BillingOptions>(resolver, appsettings, binder).GetAsync();

        binder.Calls.Should().Be(1);
        options.TaxRate.Should().Be("0.22", "the tenant's store value wins");
        options.Currency.Should().Be("EUR", "appsettings answers a key the store does not have");
        options.Region.Should().Be("default", "a key no source has leaves the default");
    }

    [Fact]
    public async Task GetAsync_WithABinder_ReturnsANewInstanceEachCall()
    {
        var resolver = new ConfigurationResolver(
            new InMemoryConfigurationStore(), EnvironmentProfile.From("Production"), new FakeTenantContext(null));
        var sut = new TenantOptions<BillingOptions>(resolver, new ConfigurationBuilder().Build(), new RecordingBinder());

        var first = await sut.GetAsync();
        var second = await sut.GetAsync();

        second.Should().NotBeSameAs(first, "a caller may change what it was given; nothing is shared");
    }

    /// <summary>
    ///     The container injects a registered binder into the open-generic <c>TenantOptions&lt;&gt;</c>, and
    ///     resolves it without one: the binder parameter is optional, not a dependency every class must have.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolvedFromTheContainer_UsesTheBinderOnlyWhenRegistered(bool registered)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfigurationResolver>(new ConfigurationResolver(
            new InMemoryConfigurationStore(), EnvironmentProfile.From("Production"), new FakeTenantContext(null)));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:Currency"] = "EUR" })
            .Build());
        services.AddScoped(typeof(ITenantOptions<>), typeof(TenantOptions<>));
        var binder = new RecordingBinder();
        if (registered)
            services.AddSingleton<ITenantOptionsBinder<BillingOptions>>(binder);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var options = await scope.ServiceProvider.GetRequiredService<ITenantOptions<BillingOptions>>().GetAsync();

        options.Currency.Should().Be("EUR");
        binder.Calls.Should().Be(registered ? 1 : 0);
    }
}
