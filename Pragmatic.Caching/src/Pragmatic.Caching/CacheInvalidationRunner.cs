using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Caching;

/// <summary>
///     Runs the invalidation a <c>[InvalidatesCache]</c> declaration asks for. Called by the domain
///     event handler the source generator emits for every event carrying the attribute.
/// </summary>
/// <remarks>
///     <para>
///         The generated handler holds only the declared data — the category, the expanded tags, the
///         expanded keys. Everything about <i>where</i> that invalidation has to land lives here, so
///         one event and fifty events emit the same two lines of generated code.
///     </para>
///     <para>
///         Resolution mirrors the mutation invoker: <see cref="ICacheStackResolver"/> when caching is
///         registered (honouring <see cref="CachingOptions.EnableEventInvalidation"/> and category
///         routing), a bare <see cref="ICacheStack"/> when it is not, and nothing at all when the
///         application never called <c>AddPragmaticCaching()</c> — an app without caching must not
///         break because one of its events declares an invalidation.
///     </para>
/// </remarks>
public static partial class CacheInvalidationRunner
{
    /// <summary>
    ///     Invalidates <paramref name="tags"/> and removes <paramref name="keys"/> on every cache
    ///     stack the declaration has to reach.
    /// </summary>
    /// <param name="services">The scope the event is being handled in.</param>
    /// <param name="category">
    ///     The cache category to route to, or <c>null</c> to reach every registered stack — the entry
    ///     could be in any of them.
    /// </param>
    /// <param name="tags">Tags to invalidate, placeholders already expanded.</param>
    /// <param name="keys">Keys to remove, placeholders already expanded.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     <para>
    ///         Every tag and key is attempted on every stack: a failure on one does not skip the rest,
    ///         and the failures are rethrown together once everything has been tried. Stopping at the
    ///         first one would leave entries the caller believes are gone — the same reasoning behind
    ///         <see cref="ICacheStack.InvalidateByTagsAsync"/>, which is not used here because it
    ///         aborts the remaining <i>stacks</i> and says nothing about keys.
    ///     </para>
    ///     <para>
    ///         <c>InMemoryEventDispatcher</c> isolates and logs a throwing handler, so the
    ///         <see cref="AggregateException"/> is how a partial invalidation becomes visible instead
    ///         of silently serving stale entries. Cancellation propagates untouched.
    ///     </para>
    /// </remarks>
    /// <exception cref="AggregateException">One or more tags or keys could not be invalidated.</exception>
    public static async Task InvalidateAsync(
        IServiceProvider services,
        Type? category,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> keys,
        CancellationToken ct = default)
    {
        ThrowIfNull(services);
        ThrowIfNull(tags);
        ThrowIfNull(keys);

        var resolver = services.GetService<ICacheStackResolver>();

        // Switched off on purpose: nothing to report.
        if (resolver is not null && !resolver.InvalidationEnabled)
            return;

        var stacks = resolver is not null
            ? resolver.ForInvalidation(category)
            : services.GetService<ICacheStack>() is { } only ? [only] : (IReadOnlyList<ICacheStack>)[];

        if (stacks.Count == 0)
        {
            // The event declared an invalidation but nothing can carry it out. Silence here means
            // stale entries served until they expire, so say it once rather than never.
            if (services.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) is { } logger)
                LogInvalidationSkipped(logger, tags.Count + keys.Count);
            return;
        }

        List<Exception>? failures = null;

        foreach (var stack in stacks)
        {
            foreach (var tag in tags)
            {
                try
                {
                    await stack.InvalidateByTagAsync(tag, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    (failures ??= []).Add(ex);
                }
            }

            foreach (var key in keys)
            {
                try
                {
                    await stack.RemoveAsync(key, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    (failures ??= []).Add(ex);
                }
            }
        }

        if (failures is { Count: > 0 })
            throw new AggregateException(
                "One or more cache entries declared by [InvalidatesCache] could not be invalidated.",
                failures);
    }

    private const string LoggerCategory = "Pragmatic.Caching.CacheInvalidationRunner";

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =
            "A domain event declared {entryCount} cache invalidation(s) via [InvalidatesCache] but no ICacheStack is registered — skipped, stale entries may be served")]
    private static partial void LogInvalidationSkipped(ILogger logger, int entryCount);
}
