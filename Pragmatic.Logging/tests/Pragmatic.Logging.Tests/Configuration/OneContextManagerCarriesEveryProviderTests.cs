using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Logging.AspNetCore;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Extensions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Configuration;

/// <summary>
///     An application that uses the ASP.NET integration and the logging builder together gets <b>one</b>
///     context manager, carrying every provider.
/// </summary>
/// <remarks>
///     <para>
///         <c>ContextProviderRegistrationService</c> registered the container's
///         <c>IContextProvider</c>s into <c>ContextManager.Instance</c>, the static one, while
///         <c>ConfigureContext</c> built and registered a manager of its own. The HTTP providers
///         landed on one, the system and declared ones on the other, and each carried half the context.
///     </para>
///     <para>
///         ⚠️ The static instance is not an implementation detail here: a log provider is constructed with
///         a name and a configuration, never from the container, so what it enriches an entry with is what
///         the <b>ambient</b> manager holds. A per-container manager that the writers cannot see would be a
///         second silence — configured, resolvable, and read by nobody.
///     </para>
/// </remarks>
[Collection(nameof(TheAmbientContextManager))]
public class OneContextManagerCarriesEveryProviderTests
{
    private sealed class TenantName
    {
        public string Value { get; } = "acme";
    }

    private sealed class TenantContextProvider(TenantName tenant) : ContextProviderBase("Tenant")
    {
        public override IReadOnlyDictionary<string, object?> GetContextProperties()
            => new Dictionary<string, object?> { ["Tenant"] = tenant.Value };
    }

    private static async Task<ServiceProvider> AnApplicationUsingBoth()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TenantName>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddPragmaticHttpLogging();
        services.AddPragmaticLoggingBuilder(logging =>
            logging.ConfigureContext(context => context.AddProvider<TenantContextProvider>()));

        var provider = services.BuildServiceProvider();

        // The HTTP providers reach a manager through the hosted service, as they do in a host.
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);

        return provider;
    }

    private static string[] NamesOf(IContextManager manager)
        => manager.GetProviders().Select(p => p.Name).ToArray();

    [Fact]
    public async Task TheResolvedManager_CarriesTheHttpProvidersAndTheDeclaredOnes()
    {
        using var provider = await AnApplicationUsingBoth().ConfigureAwait(true);

        var names = NamesOf(provider.GetRequiredService<IContextManager>());

        names.Should().Contain("Tenant", "the application declared it on the builder");
        names.Should().Contain("Machine", "the system providers are the builder's too");
        names.Should().Contain("HttpContext", "and the ASP.NET integration registered these");
        names.Should().Contain("CorrelationId");
    }

    /// <summary>
    ///     And the manager a log provider reads is that same one: a provider is built without the
    ///     container, so it enriches from the ambient manager.
    /// </summary>
    [Fact]
    public async Task TheManagerALogProviderReads_IsTheOneTheContainerAnswersWith()
    {
        using var provider = await AnApplicationUsingBoth().ConfigureAwait(true);

        var resolved = provider.GetRequiredService<IContextManager>();

        NamesOf(ContextManager.Instance).Should().BeEquivalentTo(NamesOf(resolved),
            "one process, one ambient context: what a log provider reads is what the container answers with");
        NamesOf(ContextManager.Instance).Should().Contain("Tenant").And.Contain("HttpContext");
    }
}

/// <summary>
///     The ambient context manager is process-wide, so the tests that configure it run one at a time and
///     put it back as they found it.
/// </summary>
[CollectionDefinition(nameof(TheAmbientContextManager), DisableParallelization = true)]
public class TheAmbientContextManager;
