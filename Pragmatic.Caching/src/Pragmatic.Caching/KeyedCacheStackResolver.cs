using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching;

/// <summary>
///     <see cref="ICacheStackResolver"/> over keyed DI: the service key is the category type's
///     <see cref="Type.FullName"/>, matching how <c>ForCategory&lt;T&gt;()</c> registers them.
/// </summary>
internal sealed class KeyedCacheStackResolver : ICacheStackResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CacheCategoryRegistry _registry;
    private readonly CachingOptions _options;

    public KeyedCacheStackResolver(
        IServiceProvider serviceProvider, CacheCategoryRegistry registry,
        IOptions<CachingOptions>? options = null)
    {
        ThrowIfNull(serviceProvider);
        ThrowIfNull(registry);
        _serviceProvider = serviceProvider;
        _registry = registry;
        _options = options?.Value ?? new CachingOptions();
    }

    /// <inheritdoc />
    public bool QueryCachingEnabled => _options.EnableQueryCaching;

    /// <inheritdoc />
    public bool InvalidationEnabled => _options.EnableEventInvalidation;

    /// <inheritdoc />
    public ICacheStack? ForQuery(Type? category)
        => _options.EnableQueryCaching ? Resolve(category) : null;

    /// <inheritdoc />
    public IReadOnlyList<ICacheStack> ForInvalidation(Type? category)
    {
        if (!_options.EnableEventInvalidation)
            return [];

        if (category is not null)
            return Resolve(category) is { } one ? [one] : [];

        var stacks = new List<ICacheStack>(_registry.Keys.Length + 1);

        if (_serviceProvider.GetService<ICacheStack>() is { } @default)
            stacks.Add(@default);

        foreach (var key in _registry.Keys)
        {
            if (_serviceProvider.GetKeyedService<ICacheStack>(key) is { } scoped)
                stacks.Add(scoped);
        }

        return stacks;
    }

    private ICacheStack? Resolve(Type? category)
    {
        if (category?.FullName is not { } key)
            return _serviceProvider.GetService<ICacheStack>();

        // Falling back to the default stack is what makes an unregistered category safe rather than
        // silently wrong: a category nobody called ForCategory<T>() for has no prefixed stack, so its
        // entries were written to the default one and have to be invalidated there too. Both sides
        // fall back the same way, so they keep addressing the same namespace.
        return _serviceProvider.GetKeyedService<ICacheStack>(key)
               ?? _serviceProvider.GetService<ICacheStack>();
    }

}
