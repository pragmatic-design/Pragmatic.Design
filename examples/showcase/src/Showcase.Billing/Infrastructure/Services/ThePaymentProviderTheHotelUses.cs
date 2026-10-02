using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Showcase.Billing.Infrastructure.Services;

/// <summary>
/// Which payment provider this deployment charges through.
/// </summary>
/// <remarks>
/// <para>
/// Two providers are registered, each under its own key, and something has to choose. A literal in
/// <see cref="PaymentOrchestrator"/>'s constructor — <c>[FromKeyedServices("stripe")]</c> — would be a
/// deployment decision written into the domain: switching provider would mean editing an
/// orchestrator and shipping it.
/// </para>
/// <para>
/// <c>[ServiceFactory]</c> is where a registration that has to <em>decide</em> belongs. The class is
/// registered as a singleton and each <c>[Factory]</c> method registers its return type, with the
/// method's own parameters resolved per call — so the scoped resolution below happens inside the
/// caller's scope and this singleton captures nothing.
/// </para>
/// <para>
/// ⚠️ The keyed registrations stay: the factory adds the <em>unkeyed</em> <c>IPaymentProvider</c>,
/// which is the one a module injects, and leaves "charge through this specific one" reachable for
/// the code that genuinely means a specific one.
/// </para>
/// <para>
/// ⚠️ The key is read in the constructor and not in the method, and that is not only taste: a
/// <c>[Factory]</c> method that touches no instance state is <c>CA1822</c>, which this repository
/// builds as an error — so a stateless factory method has no legal spelling here, and the class has
/// to hold something. Reading configuration once in the singleton rather than on every scoped
/// resolution is the shape that makes that true honestly.
/// </para>
/// </remarks>
[ServiceFactory]
public sealed class ThePaymentProviderTheHotelUses(IConfiguration configuration)
{
    /// <summary>The key used when configuration names none — the provider the Showcase ships with.</summary>
    public const string DefaultProviderKey = "stripe";

    /// <summary>The configuration key that names the provider.</summary>
    public const string ConfigurationKey = "Billing:PaymentProvider";

    /// <summary>The key this deployment configured, or the default.</summary>
    public string ProviderKey =>
        configuration[ConfigurationKey] is { Length: > 0 } key ? key : DefaultProviderKey;

    /// <summary>Resolves the keyed provider this deployment's configuration names.</summary>
    [Factory(Lifetime = Lifetime.Scoped)]
    public IPaymentProvider Chosen(IServiceProvider services)
        => services.GetRequiredKeyedService<IPaymentProvider>(ProviderKey);
}
