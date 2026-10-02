using Pragmatic.Actions.Cache;
using Pragmatic.Caching;

namespace Pragmatic.Actions.Invoker;

public abstract partial class DomainActionInvoker<TAction, TReturn>
{
    /// <summary>
    ///     The action as its <c>ICacheable</c> when it declared <c>[Cacheable]</c>, <c>null</c> otherwise.
    ///     Overridden by the generated invoker, which knows at compile time.
    /// </summary>
    /// <remarks>
    ///     A hook the generator fills rather than an <c>is ICacheable</c> here: whether the action is
    ///     cacheable is decided where it is known. Without the override, every invocation of a
    ///     <c>[Cacheable]</c> action would run the body: the query executor is the only other reader of
    ///     <c>ICacheable</c>.
    /// </remarks>
    /// <param name="action">The action being invoked.</param>
    protected virtual ICacheable? CacheableRead(TAction action) => null;

    private ActionCacheEntry? OpenCacheEntry(TAction action)
        => CacheableRead(action) is { } cacheable
            ? ActionCacheEntry.Open(cacheable, ServiceProvider,
                () => LogCacheReadSkipped("Action", SActionName))
            : null;
}
