namespace Pragmatic.Logging.Context;

/// <summary>
///     A handle on the process-wide <see cref="ContextManager.Instance" />, for the container to hand out.
/// </summary>
/// <remarks>
///     <para>
///         The ambient manager is what a log provider reads: a provider is constructed with a name and a
///         configuration, never from the container. So the container must answer with <em>that</em> manager,
///         or the providers an application declares are configured, resolvable, and read by nobody.
///     </para>
///     <para>
///         ⚠️ It exists because the container disposes what its factories return. Registering the ambient
///         instance directly meant the first <c>ServiceProvider</c> to be disposed took the process's context
///         manager with it, and everything registered afterwards threw
///         <see cref="ObjectDisposedException" /> — seen as a second test in the same class failing while it
///         passed alone. This wrapper is what the container owns; the manager behind it outlives it.
///     </para>
/// </remarks>
internal sealed class AmbientContextManager : IContextManager
{
    private static ContextManager Manager => ContextManager.Instance;

    /// <inheritdoc />
    public int ProviderCount => Manager.ProviderCount;

    /// <inheritdoc />
    public event EventHandler<ContextProvidersChangedEventArgs>? ProvidersChanged
    {
        add => Manager.ProvidersChanged += value;
        remove => Manager.ProvidersChanged -= value;
    }

    /// <inheritdoc />
    public void RegisterProvider(IContextProvider provider) => Manager.RegisterProvider(provider);

    /// <inheritdoc />
    public void RegisterProviders(IEnumerable<IContextProvider> providers) => Manager.RegisterProviders(providers);

    /// <inheritdoc />
    public bool UnregisterProvider(string providerName) => Manager.UnregisterProvider(providerName);

    /// <inheritdoc />
    public IContextProvider[] GetProviders() => Manager.GetProviders();

    /// <inheritdoc />
    public bool HasProvider(string providerName) => Manager.HasProvider(providerName);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetContextProperties() => Manager.GetContextProperties();

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, object?>> GetAggregateContextAsync(CancellationToken cancellationToken = default)
        => Manager.GetAggregateContextAsync(cancellationToken);

    /// <inheritdoc />
    public Task<object?> GetContextPropertyAsync(string providerName, string propertyName, CancellationToken cancellationToken = default)
        => Manager.GetContextPropertyAsync(providerName, propertyName, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, object?>> GetProviderContextAsync(string providerName, CancellationToken cancellationToken = default)
        => Manager.GetProviderContextAsync(providerName, cancellationToken);

    /// <inheritdoc />
    public void InvalidateCache() => Manager.InvalidateCache();
}
