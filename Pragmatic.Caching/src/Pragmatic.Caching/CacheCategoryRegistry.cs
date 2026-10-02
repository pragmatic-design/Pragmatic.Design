using System.Collections.Immutable;

namespace Pragmatic.Caching;

/// <summary>
///     The categories that were registered with <c>ForCategory&lt;T&gt;()</c>, so that an
///     invalidation naming no category can reach all of them.
/// </summary>
/// <remarks>
///     Keyed services cannot be enumerated: the container answers "give me the stack for this key"
///     and never "which keys exist". Broadcast therefore needs the list kept at registration time,
///     which is the only moment it is known.
/// </remarks>
internal sealed class CacheCategoryRegistry
{
    /// <summary>An empty registry — no categories configured.</summary>
    public static readonly CacheCategoryRegistry Empty = new([]);

    /// <summary>Creates a registry over the given keyed-service keys (category <c>FullName</c>s).</summary>
    public CacheCategoryRegistry(ImmutableArray<string> keys) => Keys = keys;

    /// <summary>The keyed-service keys, one per registered category.</summary>
    public ImmutableArray<string> Keys { get; }
}
