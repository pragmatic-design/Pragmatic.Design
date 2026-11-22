using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Caching;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Caching;

/// <summary>
///     Reloads a <c>[Lookup]</c> entity's cache once a write to it has committed (#734), on every engine (#881).
/// </summary>
/// <remarks>
///     <para>
///         The cache was loaded at startup and by a tenant appearing, and by no write: a row added or changed through the application
///         was not seen by a lookup navigation until the process restarted.
///     </para>
///     <para>
///         The reload reads the table through a scope of its own, so it waits for the commit. That is what <see cref="ICommitObserver.Committed" />
///         is: EF Core calls it after a save with no transaction or after the transaction's commit, the Sql and documents engines after their
///         session's commit, and none of them after a rollback.
///     </para>
///     <para>
///         Then it invalidates the lookup's <see cref="LookupCacheInvalidationTag" /> through <see cref="ICacheStack" /> (#763). On one
///         node that removes nothing; with a distributed invalidation broadcast (<c>Pragmatic.Caching.Redis</c>) the tag reaches every other
///         instance of the host, whose <see cref="LookupCacheInvalidationObserver" /> reloads the same lookup there. Without a cache stack
///         there is nothing to tell, and only this process's cache is reloaded.
///     </para>
///     <para>
///         A reload that fails is logged and not thrown, and so is a tag that cannot be invalidated: the write it follows has committed,
///         and an exception from the commit would report it as one that did not.
///     </para>
/// </remarks>
public sealed partial class LookupCacheReloadObserver : ICommitObserver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, ILookupCacheLoader> _loaders;
    private readonly ICacheStack? _cacheStack;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates the observer over the loaders the host registered, one per <c>[Lookup]</c> entity, and the cache stack the other nodes
    ///     hear the reload through, if the host has one.
    /// </summary>
    public LookupCacheReloadObserver(
        IServiceProvider serviceProvider,
        IEnumerable<ILookupCacheLoader> loaders,
        ILogger<LookupCacheReloadObserver>? logger = null,
        ICacheStack? cacheStack = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(loaders);
        _serviceProvider = serviceProvider;
        _loaders = loaders.ToDictionary(l => l.EntityType);
        _cacheStack = cacheStack;
        _logger = logger ?? NullLogger<LookupCacheReloadObserver>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>Nothing to refuse: a lookup's write is reloaded after it, never checked before it.</remarks>
    public void Writing(IReadOnlyList<CommitEntry> entries)
    {
    }

    /// <inheritdoc />
    public void Committed(IReadOnlyList<CommitEntry> entries)
    {
        HashSet<(Type Entity, string? Tenant)>? written = null;
        foreach (var entry in entries)
        {
            var type = entry.Entity.GetType();
            if (_loaders.ContainsKey(type))
                (written ??= []).Add((type, (entry.Entity as ITenantEntity)?.TenantId));
        }

        if (written is null)
            return;

        // The commit is synchronous on every engine and the loaders are not: Task.Run detaches from an ambient SynchronizationContext
        // before blocking, as CachedPermissionResolver does for its providers.
        Task.Run(() => ReloadAsync(written, CancellationToken.None)).GetAwaiter().GetResult();
    }

    private async Task ReloadAsync(HashSet<(Type Entity, string? Tenant)> written, CancellationToken ct)
    {
        foreach (var (entity, tenant) in written)
        {
            try
            {
                await _loaders[entity].ReloadAsync(_serviceProvider, tenant, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReloadFailed(_logger, ex, entity.Name, tenant ?? "(none)");
            }
        }

        if (_cacheStack is null)
            return;

        // Once per entity, after this node's reload: the other nodes reload every tenant of it, so naming it twice is the same reload twice.
        foreach (var entity in written.Select(w => w.Entity).Distinct())
        {
            var tag = LookupCacheInvalidationTag.For(entity);
            try
            {
                await _cacheStack.InvalidateByTagAsync(tag, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBroadcastFailed(_logger, ex, entity.Name, tag);
            }
        }
    }

    [LoggerMessage(
        EventId = 6103,
        Level = LogLevel.Error,
        Message = "The lookup cache of {Entity} (tenant {TenantId}) failed to reload after a committed write. Lookup navigations " +
                  "read the rows it held before the write until the next reload or a restart.")]
    private static partial void LogReloadFailed(ILogger logger, Exception ex, string entity, string tenantId);

    [LoggerMessage(
        EventId = 6104,
        Level = LogLevel.Error,
        Message = "The lookup cache of {Entity} reloaded here, but its tag {Tag} could not be invalidated: the other instances of the host " +
                  "keep the rows they loaded until their next reload or a restart.")]
    private static partial void LogBroadcastFailed(ILogger logger, Exception ex, string entity, string tag);
}
