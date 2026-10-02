using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;

namespace Pragmatic.Actions.Cache;

/// <summary>
///     Runs the <c>[InvalidatesCache]</c> declaration of an operation that has just committed, against
///     the stacks its category is routed to.
/// </summary>
/// <remarks>
///     <para>
///         One implementation for every kind of operation, because the routing is the part that is easy
///         to get subtly wrong and impossible to notice: a category's stack prefixes both keys and tags,
///         so an invalidation has to be issued through the same instance the entry was written through,
///         and an operation that names no category has to reach <b>every</b> registered stack because
///         the entry could be in any of them.
///     </para>
///     <para>
///         ⚠️ It lives here rather than in <c>MutationInvoker</c> — where it was — because only mutations
///         ever ran it. A <c>[DomainAction]</c> carrying <c>[InvalidatesCache]</c> got a generated,
///         correct <c>ICacheInvalidator</c> that nothing called, so the action wrote its rows and every
///         <c>[Cacheable]</c> read of them stayed stale for the whole cache duration, with no diagnostic
///         and no log.
///     </para>
/// </remarks>
internal static class CacheInvalidation
{
    /// <summary>
    ///     Invalidates for <paramref name="operation" /> if it declares an invalidation at all.
    /// </summary>
    /// <param name="operation">The mutation or action that has just committed.</param>
    /// <param name="serviceProvider">Where the cache stacks and their resolver are looked up.</param>
    /// <param name="onNoStackRegistered">
    ///     Called when the operation asked to invalidate and there is no stack to invalidate against —
    ///     the caller logs it in its own voice rather than serving stale entries in silence. Not called
    ///     when invalidation is switched off by configuration, which is a deliberate choice and not a
    ///     misconfiguration.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task RunAsync(
        object operation,
        IServiceProvider serviceProvider,
        Action onNoStackRegistered,
        CancellationToken ct)
    {
        if (operation is not ICacheInvalidator invalidator)
            return;

        var resolver = serviceProvider.GetService<ICacheStackResolver>();

        // Switched off on purpose: nothing to warn about.
        if (resolver is not null && !resolver.InvalidationEnabled)
            return;

        var stacks = resolver is not null
            ? resolver.ForInvalidation(invalidator.InvalidationCategory)
            : serviceProvider.GetService<ICacheStack>() is { } only
                ? [only]
                : (IReadOnlyList<ICacheStack>)[];

        if (stacks.Count == 0)
        {
            onNoStackRegistered();
            return;
        }

        foreach (var stack in stacks)
            await invalidator.InvalidateAsync(stack, ct).ConfigureAwait(false);
    }
}
