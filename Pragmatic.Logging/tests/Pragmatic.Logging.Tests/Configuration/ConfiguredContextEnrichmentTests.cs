using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Logging.Configuration;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Extensions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Configuration;

/// <summary>
/// What <c>ConfigureContext</c> configures reaches the context manager the application resolves.
/// </summary>
/// <remarks>
///     ⚠️ Calling <c>Services.Configure&lt;ContextConfiguration&gt;</c> is not enough when nothing reads
///     the result: every switch on it, and <c>CustomProviders</c>, would be written by an application and
///     have no effect, while the documentation presents the method as the way to configure enrichment.
///     The case that says it is the one where a flag says "off": a check that only counted providers would
///     pass on a manager that registers all three regardless.
/// </remarks>
[Collection(nameof(TheAmbientContextManager))]
public class ConfiguredContextEnrichmentTests
{
    private static ServiceProvider Build(Action<ContextConfiguration> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TenantName>();
        services.AddPragmaticLoggingBuilder(logging => logging.ConfigureContext(configure));
        return services.BuildServiceProvider();
    }

    private static string[] ProviderNames(IContextManager manager)
        => manager.GetProviders().Select(p => p.Name).ToArray();

    [Fact]
    public void ConfigureContext_WithMachineContextOff_LeavesTheManagerWithoutIt()
    {
        using var provider = Build(context => context.IncludeMachineContext = false);

        var manager = provider.GetRequiredService<IContextManager>();

        ProviderNames(manager).Should().NotContain("Machine");
        ProviderNames(manager).Should().Contain("Process");
        ProviderNames(manager).Should().Contain("Thread");
    }

    [Fact]
    public void ConfigureContext_WithDefaults_RegistersMachineProcessAndThread()
    {
        using var provider = Build(_ => { });

        ProviderNames(provider.GetRequiredService<IContextManager>())
            .Should().BeEquivalentTo(["Machine", "Process", "Thread"]);
    }

    [Fact]
    public void ConfigureContext_WithEnrichmentOff_RegistersNoProvider()
    {
        using var provider = Build(context => context.EnableEnrichment = false);

        provider.GetRequiredService<IContextManager>().ProviderCount.Should().Be(0);
    }

    [Fact]
    public void AddProvider_RegistersTheProvider_ResolvedFromTheContainer()
    {
        using var provider = Build(context => context.AddProvider<TenantContextProvider>());

        var manager = provider.GetRequiredService<IContextManager>();
        var tenant = manager.GetProviders().Single(p => p.Name == "Tenant");

        // Resolved, not constructed: its dependency comes from the container.
        tenant.GetContextProperties()["Tenant"].Should().Be("acme");
    }

    [Fact]
    public void ConfigureContext_WithCorrelationIdOff_TurnsItOffInTheProviderConfiguration()
    {
        using var provider = Build(context => context.EnableCorrelationId = false);

        var options = provider.GetRequiredService<IOptions<PragmaticLoggingOptions>>().Value;

        options.Context.IncludeCorrelationId.Should().BeFalse();
    }

    /// <summary>
    /// The two switches the documentation showed and the class never had: they describe what a log
    /// provider writes, so they reach the provider configuration rather than the manager's providers.
    /// </summary>
    [Fact]
    public void ConfigureContext_WithUserAndRequestContextOff_TurnsThemOffInTheProviderConfiguration()
    {
        using var provider = Build(context =>
        {
            context.IncludeUserContext = false;
            context.IncludeRequestContext = false;
        });

        var options = provider.GetRequiredService<IOptions<PragmaticLoggingOptions>>().Value;

        options.Context.IncludeUserContext.Should().BeFalse();
        options.Context.IncludeRequestContext.Should().BeFalse();
    }

    private sealed class TenantName
    {
        public string Value { get; } = "acme";
    }

    private sealed class TenantContextProvider(TenantName tenant) : ContextProviderBase("Tenant")
    {
        public override IReadOnlyDictionary<string, object?> GetContextProperties()
            => new Dictionary<string, object?> { ["Tenant"] = tenant.Value };
    }
}
