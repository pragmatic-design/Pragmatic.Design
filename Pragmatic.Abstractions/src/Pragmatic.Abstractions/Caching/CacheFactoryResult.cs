// ReSharper disable once CheckNamespace
namespace Pragmatic.Caching;

/// <summary>
///     Result from a cache factory that can signal whether the value should be cached.
///     Use <see cref="Cache"/> to store the value or <see cref="DoNotCache"/> to return
///     it without caching (e.g., error/fallback results that should not pollute the cache).
/// </summary>
/// <typeparam name="T">The type of value produced by the factory.</typeparam>
public readonly struct CacheFactoryResult<T>
{
    /// <summary>The value produced by the factory.</summary>
    public T Value { get; }

    /// <summary>Whether the value should be stored in the cache.</summary>
    public bool ShouldCache { get; }

    private CacheFactoryResult(T value, bool shouldCache)
    {
        Value = value;
        ShouldCache = shouldCache;
    }

    /// <summary>Creates a result that will be cached.</summary>
    /// <param name="value">The value to return and store in the cache.</param>
    /// <returns>A result whose <see cref="ShouldCache"/> is <c>true</c>.</returns>
    public static CacheFactoryResult<T> Cache(T value) => new(value, true);

    /// <summary>Creates a result that will NOT be cached — returned to the caller but not stored.</summary>
    /// <param name="value">The value to return without storing it in the cache.</param>
    /// <returns>A result whose <see cref="ShouldCache"/> is <c>false</c>.</returns>
    public static CacheFactoryResult<T> DoNotCache(T value) => new(value, false);

    /// <summary>Implicit conversion from <typeparamref name="T"/> — defaults to caching.</summary>
    public static implicit operator CacheFactoryResult<T>(T value) => Cache(value);
}
