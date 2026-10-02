namespace Pragmatic.Logging.Context;

/// <summary>
/// Manages context providers and provides aggregate context information for log enrichment.
/// Abstraction that enables dependency injection and improves testability over singleton pattern.
/// </summary>
/// <remarks>
/// <para>
/// This interface abstracts the context management functionality to enable:
/// </para>
/// <list type="bullet">
/// <item><description><strong>Dependency Injection</strong>: Register as scoped/singleton service in DI container</description></item>
/// <item><description><strong>Unit Testing</strong>: Easy mocking and testing with controlled providers</description></item>
/// <item><description><strong>Configuration</strong>: Different implementations for different environments</description></item>
/// <item><description><strong>Extensibility</strong>: Custom implementations for specialized scenarios</description></item>
/// </list>
/// <para>
/// <strong>Migration Path:</strong> Existing code using <c>ContextManager.Instance</c> can gradually
/// migrate to dependency injection without breaking changes.
/// </para>
/// </remarks>
/// <example>
/// Dependency injection registration:
/// <code>
/// services.AddSingleton&lt;IContextManager, ContextManager&gt;();
/// 
/// // Or with pre-configured providers
/// services.AddSingleton&lt;IContextManager&gt;(serviceProvider =&gt; 
/// {
///     var contextManager = new ContextManager();
///     contextManager.RegisterProvider(new TenantContextProvider());
///     contextManager.RegisterProvider(new UserContextProvider());
///     return contextManager;
/// });
/// </code>
/// 
/// Usage in classes:
/// <code>
/// public class MyLoggingService
/// {
///     private readonly IContextManager _contextManager;
///     
///     public MyLoggingService(IContextManager contextManager)
///     {
///         _contextManager = contextManager;
///     }
///     
///     public async Task LogWithContextAsync()
///     {
///         var context = await _contextManager.GetAggregateContextAsync();
///         // Use context in logging...
///     }
/// }
/// </code>
/// 
/// Unit testing:
/// <code>
/// [Test]
/// public async Task TestLoggingWithMockContext()
/// {
///     var mockContextManager = new Mock&lt;IContextManager&gt;();
///     mockContextManager
///         .Setup(x =&gt; x.GetAggregateContextAsync(It.IsAny&lt;CancellationToken&gt;()))
///         .ReturnsAsync(new Dictionary&lt;string, object?&gt; { ["TestProperty"] = "TestValue" });
///         
///     var service = new MyLoggingService(mockContextManager.Object);
///     await service.LogWithContextAsync();
///     
///     mockContextManager.Verify(x =&gt; x.GetAggregateContextAsync(It.IsAny&lt;CancellationToken&gt;()), Times.Once);
/// }
/// </code>
/// </example>
public interface IContextManager
{
    /// <summary>
    /// Registers a context provider.
    /// </summary>
    /// <param name="provider">The provider to register</param>
    /// <remarks>
    /// <para>
    /// Providers with the same name will replace existing providers.
    /// Providers are automatically sorted by priority after registration.
    /// </para>
    /// </remarks>
    void RegisterProvider(IContextProvider provider);

    /// <summary>
    /// Unregisters a context provider by name.
    /// </summary>
    /// <param name="providerName">The name of the provider to unregister</param>
    /// <returns>True if a provider was removed, false otherwise</returns>
    bool UnregisterProvider(string providerName);

    /// <summary>
    /// Gets all registered providers.
    /// </summary>
    /// <returns>Array of registered providers sorted by priority</returns>
    IContextProvider[] GetProviders();

    /// <summary>
    /// Gets aggregated context properties from all available providers (synchronous).
    /// </summary>
    /// <returns>Dictionary containing all context properties</returns>
    /// <remarks>
    /// <para>
    /// This method collects context from all registered providers that support
    /// synchronous context retrieval. Providers that only support async context
    /// will be skipped.
    /// </para>
    /// <para>
    /// For performance-critical scenarios where async context is not needed.
    /// </para>
    /// </remarks>
    IReadOnlyDictionary<string, object?> GetContextProperties();

    /// <summary>
    /// Gets aggregated context properties from all providers asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary containing all context properties</returns>
    /// <remarks>
    /// <para>
    /// This method collects context from all registered providers, including those
    /// that require async operations (e.g., HTTP context, database lookups, external API calls).
    /// </para>
    /// <para>
    /// Recommended for comprehensive context collection in scenarios where
    /// async operations are acceptable.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<string, object?>> GetAggregateContextAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the context cache, forcing fresh retrieval on next access.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Useful when context providers have changed or when dynamic context
    /// needs to be refreshed (e.g., user authentication changes, tenant switching).
    /// </para>
    /// </remarks>
    void InvalidateCache();

    /// <summary>
    /// Gets context property by name from a specific provider.
    /// </summary>
    /// <param name="providerName">Name of the provider to query</param>
    /// <param name="propertyName">Name of the property to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Property value or null if not found</returns>
    /// <remarks>
    /// <para>
    /// Allows targeted context retrieval from specific providers without
    /// collecting full context from all providers. Useful for performance
    /// optimization when only specific context is needed.
    /// </para>
    /// </remarks>
    Task<object?> GetContextPropertyAsync(string providerName, string propertyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets context properties from a specific provider.
    /// </summary>
    /// <param name="providerName">Name of the provider to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Properties from the specified provider or empty dictionary if not found</returns>
    Task<IReadOnlyDictionary<string, object?>> GetProviderContextAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers multiple providers in a single operation.
    /// </summary>
    /// <param name="providers">Collection of providers to register</param>
    /// <remarks>
    /// <para>
    /// More efficient than multiple individual <see cref="RegisterProvider"/> calls
    /// as it only sorts providers once after all registrations.
    /// </para>
    /// </remarks>
    void RegisterProviders(IEnumerable<IContextProvider> providers);

    /// <summary>
    /// Checks if a provider with the specified name is registered.
    /// </summary>
    /// <param name="providerName">Name of the provider to check</param>
    /// <returns>True if provider is registered, false otherwise</returns>
    bool HasProvider(string providerName);

    /// <summary>
    /// Gets the count of registered providers.
    /// </summary>
    int ProviderCount { get; }

    /// <summary>
    /// Event raised when providers are modified (added/removed).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Useful for components that need to react to context provider changes,
    /// such as caching strategies or monitoring systems.
    /// </para>
    /// </remarks>
    event EventHandler<ContextProvidersChangedEventArgs>? ProvidersChanged;
}

/// <summary>
/// Event arguments for context provider changes.
/// </summary>
public sealed class ContextProvidersChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the type of change that occurred.
    /// </summary>
    public ContextProviderChangeType ChangeType { get; }

    /// <summary>
    /// Gets the name of the provider that was affected.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets the provider instance for add operations, null for remove operations.
    /// </summary>
    public IContextProvider? Provider { get; }

    /// <summary>
    /// Initializes a new instance of the ContextProvidersChangedEventArgs class.
    /// </summary>
    /// <param name="changeType">Type of change</param>
    /// <param name="providerName">Name of the affected provider</param>
    /// <param name="provider">Provider instance (null for remove operations)</param>
    public ContextProvidersChangedEventArgs(ContextProviderChangeType changeType, string providerName, IContextProvider? provider = null)
    {
        ChangeType = changeType;
        ProviderName = providerName;
        Provider = provider;
    }
}

/// <summary>
/// Defines the types of context provider changes.
/// </summary>
public enum ContextProviderChangeType
{
    /// <summary>A provider was added or replaced.</summary>
    Added,

    /// <summary>A provider was removed.</summary>
    Removed,

    /// <summary>Multiple providers were added in bulk.</summary>
    BulkAdded,

    /// <summary>All providers were cleared.</summary>
    Cleared
}