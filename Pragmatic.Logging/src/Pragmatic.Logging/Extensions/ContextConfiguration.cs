using System.Diagnostics.CodeAnalysis;
using Pragmatic.Logging.Context;

namespace Pragmatic.Logging.Extensions;

/// <summary>
/// Configuration for context enrichment, as an application states it on
/// <see cref="PragmaticLoggingBuilder.ConfigureContext" />.
/// </summary>
/// <remarks>
///     ⚠️ Not registered as options: nothing would read them. What it declares reaches the
///     <see cref="IContextManager" />
///     the application resolves, and a provider is declared by its type, not by a <c>Type</c> object:
///     <see cref="AddProvider{TProvider}()" />.
/// </remarks>
public sealed class ContextConfiguration
{
    private readonly List<ContextProviderRegistration> _providers = [];

    /// <summary>
    /// Gets or sets whether to enable automatic context enrichment. When false the context manager
    /// registers no provider at all, the declared ones included.
    /// </summary>
    public bool EnableEnrichment { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable correlation ID tracking. It travels to the provider
    /// configuration the log providers read, as <c>IncludeCorrelationId</c>.
    /// </summary>
    public bool EnableCorrelationId { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include user context. Like <see cref="EnableCorrelationId" />, it
    /// describes what a log provider writes and travels to the provider configuration.
    /// </summary>
    public bool IncludeUserContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include request context.
    /// </summary>
    public bool IncludeRequestContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include machine context.
    /// </summary>
    public bool IncludeMachineContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include process context.
    /// </summary>
    public bool IncludeProcessContext { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include thread context.
    /// </summary>
    public bool IncludeThreadContext { get; set; } = true;

    /// <summary>
    /// Adds a context provider of the application's own, constructed by the service container.
    /// </summary>
    /// <typeparam name="TProvider">The provider type, registered as a singleton unless it already is.</typeparam>
    /// <returns>The configuration for chaining.</returns>
    public ContextConfiguration AddProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>()
        where TProvider : class, IContextProvider
    {
        _providers.Add(ContextProviderRegistration.Of<TProvider>());
        return this;
    }

    /// <summary>
    /// Adds a context provider built by a factory, for one the container cannot construct on its own.
    /// </summary>
    /// <typeparam name="TProvider">The provider type.</typeparam>
    /// <param name="factory">Builds the provider from the service provider.</param>
    /// <returns>The configuration for chaining.</returns>
    public ContextConfiguration AddProvider<TProvider>(Func<IServiceProvider, TProvider> factory)
        where TProvider : class, IContextProvider
    {
        ArgumentNullException.ThrowIfNull(factory);

        _providers.Add(ContextProviderRegistration.Of(factory));
        return this;
    }

    /// <summary>The providers this configuration declares, in the order they were added.</summary>
    internal IReadOnlyList<ContextProviderRegistration> Providers => _providers;
}
