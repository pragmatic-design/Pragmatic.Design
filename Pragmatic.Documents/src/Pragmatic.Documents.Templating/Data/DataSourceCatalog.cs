namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// A registry of named data source providers. Composes N sources into a single
/// <see cref="TemplateDataContext"/> via <see cref="ToDataContext"/>.
/// Source names are unique — duplicates throw <see cref="ArgumentException"/>.
/// </summary>
public sealed class DataSourceCatalog
{
    private readonly Dictionary<string, IDataSourceProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registered source names.</summary>
    public IReadOnlyCollection<string> SourceNames => _providers.Keys;

    /// <summary>Number of registered sources.</summary>
    public int Count => _providers.Count;

    /// <summary>Add a typed static data source.</summary>
    public DataSourceCatalog Add<T>(string name, T value) where T : class
        => AddProvider(new StaticDataSource<T>(name, value));

    /// <summary>Add a typed async data source.</summary>
    public DataSourceCatalog AddAsync<T>(string name, Func<CancellationToken, ValueTask<T>> factory) where T : class
        => AddProvider(new AsyncDataSource<T>(name, factory));

    /// <summary>Add a custom provider.</summary>
    public DataSourceCatalog AddProvider(IDataSourceProvider provider)
    {
        if (_providers.ContainsKey(provider.Name))
            throw new ArgumentException($"Data source '{provider.Name}' is already registered. Use Merge with allowOverride to replace.", nameof(provider));

        _providers[provider.Name] = provider;
        return this;
    }

    /// <summary>
    /// Merge another catalog into this one.
    /// Duplicate names throw unless <paramref name="allowOverride"/> is true.
    /// </summary>
    public DataSourceCatalog Merge(DataSourceCatalog other, bool allowOverride = false)
    {
        foreach (var (name, provider) in other._providers)
        {
            if (_providers.ContainsKey(name) && !allowOverride)
                throw new ArgumentException($"Data source '{name}' exists in both catalogs. Set allowOverride=true to replace.");

            _providers[name] = provider;
        }
        return this;
    }

    /// <summary>Check if a source with the given name is registered.</summary>
    public bool HasSource(string name) => _providers.ContainsKey(name);

    /// <summary>Get the provider for a given source name.</summary>
    public IDataSourceProvider? GetProvider(string name)
        => _providers.GetValueOrDefault(name);

    /// <summary>
    /// Convert this catalog to a <see cref="TemplateDataContext"/>.
    /// Each provider is registered as an async source (resolved on first access, cached).
    /// </summary>
    public TemplateDataContext ToDataContext(string? culture = null)
    {
        var ctx = new TemplateDataContext();

        if (culture is not null)
            ctx.WithCulture(culture);

        foreach (var (name, provider) in _providers)
        {
            // Static sources: register directly for immediate access
            if (provider is IStaticDataSource staticSource)
            {
                ctx.AddSource(name, staticSource.GetValue());
            }
            else
            {
                // Async sources: register as async factory (resolved on demand, cached by TemplateDataContext)
                ctx.AddSource(name, async ct => await provider.ResolveAsync(ct));
            }
        }

        return ctx;
    }
}
