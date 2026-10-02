using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Billing.Infrastructure.Services;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     <c>[ServiceFactory]</c> and <c>[Factory]</c>: a registration that has to decide.
/// </summary>
/// <remarks>
///     <para>
///         Two payment providers are registered under their own keys, and the unkeyed
///         <c>IPaymentProvider</c> — the one a module injects — is registered by
///         <see cref="ThePaymentProviderTheHotelUses" />, whose <c>[Factory]</c> method reads the
///         configured key. Before this the choice was <c>[FromKeyedServices("stripe")]</c> in
///         <see cref="PaymentOrchestrator" />'s constructor: a deployment decision written into the
///         domain.
///     </para>
///     <para>
///         ⚠️ The factory class is declared in the <b>module</b> and registered by the <b>host</b>:
///         Billing emits it as metadata and the host's generated
///         <c>AddPragmaticServiceFactories()</c> registers it. So "the unkeyed provider resolves" is
///         an assertion about that channel and not only about the attribute compiling.
///     </para>
/// </remarks>
public class TheProviderTheConfigurationNamesTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public void TheUnkeyedProvider_IsTheOneTheFactoryChose()
    {
        using var scope = Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<IPaymentProvider>();

        provider.ProviderName.Should().Be("Stripe",
            "nothing registers an unkeyed IPaymentProvider except the [Factory] method, and with no "
            + "configured key it falls back to the provider the Showcase ships with");
    }

    /// <summary>
    ///     The control: the keys still tell the two providers apart.
    /// </summary>
    /// <remarks>
    ///     Without it, "the unkeyed provider is Stripe" is satisfied by a container in which every
    ///     <c>IPaymentProvider</c> is Stripe — which is what a factory resolving the wrong key, or a
    ///     keyed registration that stopped being keyed, would look like.
    /// </remarks>
    [Fact]
    public void TheKeyedProviders_AreStillTwo()
    {
        using var scope = Services.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IPaymentProvider>("stripe")
            .ProviderName.Should().Be("Stripe");
        scope.ServiceProvider.GetRequiredKeyedService<IPaymentProvider>("bank")
            .ProviderName.Should().Be("BankTransfer");
    }

    /// <summary>
    ///     The configured key is what the method reads — measured on the factory itself, because one
    ///     host has one configuration.
    /// </summary>
    /// <remarks>
    ///     The host's own singleton answers the default; the configured case is built on a
    ///     configuration of its own, because one host has one configuration and this is the half that
    ///     needs a second. Asserting only the default would leave "it reads configuration" untested: a
    ///     method returning the Stripe provider outright answers the first case identically.
    /// </remarks>
    [Fact]
    public void AConfiguredKey_ChoosesTheOtherProvider()
    {
        using var scope = Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ThePaymentProviderTheHotelUses>()
            .ProviderKey.Should().Be(ThePaymentProviderTheHotelUses.DefaultProviderKey,
                "the host the tests run against configures none, and the singleton is registered");

        new ThePaymentProviderTheHotelUses(Naming("bank")).Chosen(scope.ServiceProvider)
            .ProviderName.Should().Be("BankTransfer");
        new ThePaymentProviderTheHotelUses(Naming(null)).Chosen(scope.ServiceProvider)
            .ProviderName.Should().Be("Stripe", "and an unset key still falls back");
    }

    private static IConfiguration Naming(string? key)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(key is null
                ? []
                : new Dictionary<string, string?>
                {
                    [ThePaymentProviderTheHotelUses.ConfigurationKey] = key
                })
            .Build();
}
