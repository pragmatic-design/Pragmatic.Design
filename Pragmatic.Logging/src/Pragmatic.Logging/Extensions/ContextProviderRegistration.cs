using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Logging.Context;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// A context provider an application declared on <see cref="ContextConfiguration" />: how to register it
/// in the container, and how to read it back from there.
/// </summary>
/// <remarks>
///     Two typed delegates rather than a <c>Type</c>. A list of <c>Type</c> can only be turned into
///     instances by reflection, which the framework does not do in new code.
/// </remarks>
internal sealed class ContextProviderRegistration(
    Action<IServiceCollection> register,
    Func<IServiceProvider, IContextProvider> resolve)
{
    /// <summary>Declares the provider's own type in the container, unless something already did.</summary>
    public void Register(IServiceCollection services) => register(services);

    /// <summary>The instance the container answers with, with its dependencies injected.</summary>
    public IContextProvider Resolve(IServiceProvider services) => resolve(services);

    /// <summary>A provider the container constructs.</summary>
    /// <remarks>
    ///     The container reads the type's public constructors to build it, so the requirement is declared
    ///     on the type parameter and travels to whoever names the provider — rather than stopping here and
    ///     failing in a trimmed build.
    /// </remarks>
    public static ContextProviderRegistration Of<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>()
        where TProvider : class, IContextProvider
        => new(services => services.TryAddSingleton<TProvider>(),
               services => services.GetRequiredService<TProvider>());

    /// <summary>A provider a factory constructs, for what the container cannot build on its own.</summary>
    public static ContextProviderRegistration Of<TProvider>(Func<IServiceProvider, TProvider> factory)
        where TProvider : class, IContextProvider
        => new(services => services.TryAddSingleton(factory),
               services => services.GetRequiredService<TProvider>());
}
