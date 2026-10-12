using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     Without a <c>SectionPath</c>, <see cref="TenantOptions{T}" /> and a change handler read the section the
///     generated registration binds <c>IOptions&lt;T&gt;</c> to: the class name without its <c>Options</c> suffix,
///     and nothing else stripped (#194).
/// </summary>
/// <remarks>
///     The runtime also stripped <c>Settings</c>, <c>Configuration</c> and <c>Config</c>, so for
///     <c>PaymentSettings</c> the tenant read the section <c>"Payment"</c> while <c>IOptions</c> read
///     <c>"PaymentSettings"</c>. The generator's side is pinned by <c>SectionPathInferenceTests</c>.
/// </remarks>
public class TheRuntimeInfersTheSectionTheGeneratorInfersTests
{
    public sealed class PaymentSettings
    {
        public string Currency { get; set; } = "";
    }

    public sealed class SmtpConfiguration
    {
        public string Currency { get; set; } = "";
    }

    public sealed class MyConfig
    {
        public string Currency { get; set; } = "";
    }

    public sealed class BookingOptions
    {
        public string Currency { get; set; } = "";
    }

    private sealed class NoTenant : ITenantContext
    {
        public string? TenantId => null;
        public string? TenantName => null;
        public bool IsResolved => false;
    }

    private sealed class Handler<T> : IConfigurationChangeHandler<T>
        where T : class
    {
        public Task OnChangedAsync(ConfigurationChange change, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public Task TenantOptions_PaymentSettings_ReadsPaymentSettings() => ReadsSection<PaymentSettings>("PaymentSettings");

    [Fact]
    public Task TenantOptions_SmtpConfiguration_ReadsSmtpConfiguration() => ReadsSection<SmtpConfiguration>("SmtpConfiguration");

    [Fact]
    public Task TenantOptions_MyConfig_ReadsMyConfig() => ReadsSection<MyConfig>("MyConfig");

    [Fact]
    public Task TenantOptions_BookingOptions_ReadsBooking() => ReadsSection<BookingOptions>("Booking");

    [Fact]
    public void ChangeHandler_PaymentSettings_WatchesPaymentSettings() => WatchesSection<PaymentSettings>("PaymentSettings");

    [Fact]
    public void ChangeHandler_MyConfig_WatchesMyConfig() => WatchesSection<MyConfig>("MyConfig");

    [Fact]
    public void ChangeHandler_BookingOptions_WatchesBooking() => WatchesSection<BookingOptions>("Booking");

    private static async Task ReadsSection<T>(string section)
        where T : class, new()
    {
        var resolver = new ConfigurationResolver(new InMemoryConfigurationStore(), EnvironmentProfile.From("Production"), new NoTenant());
        var appsettings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{section}:Currency"] = "EUR" })
            .Build();

        var options = await new TenantOptions<T>(resolver, appsettings).GetAsync();

        typeof(T).GetProperty("Currency")!.GetValue(options).Should().Be("EUR", $"{typeof(T).Name} is bound to the section \"{section}\"");
    }

    private static void WatchesSection<T>(string section)
        where T : class
    {
        var services = new ServiceCollection();

        services.AddConfigurationChangeHandler<T, Handler<T>>();

        services.Select(d => d.ImplementationInstance).OfType<ConfigurationChangeSubscription>()
            .Should().ContainSingle().Which.Section.Should().Be(section);
    }
}
