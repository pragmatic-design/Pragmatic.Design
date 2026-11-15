using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Caching;

namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     Reloads a <c>[Lookup]</c> entity's cache once a write to it has committed (#734).
/// </summary>
/// <remarks>
///     <para>
///         The cache was loaded at startup and by a tenant appearing, and by no write: a row added or changed through the application
///         was not seen by a lookup navigation until the process restarted.
///     </para>
///     <para>
///         The reload reads the table through a context of its own, so it waits for the commit: at <c>SavedChanges</c> when the save
///         ran in no transaction, at <c>TransactionCommitted</c> when it ran in one (the unit of work's, or the one a roll-up opens). A
///         transaction rolled back reloads nothing. The written types are collected at <c>SavingChanges</c>, while the entries still
///         say what was added, changed or deleted.
///     </para>
///     <para>
///         Then it invalidates the lookup's <see cref="LookupCacheInvalidationTag" /> through <see cref="ICacheStack" /> (#763). On one
///         node that removes nothing; with a distributed invalidation broadcast (<c>Pragmatic.Caching.Redis</c>) the tag reaches every other
///         instance of the host, whose <see cref="LookupCacheInvalidationObserver" /> reloads the same lookup there. Without a cache stack
///         there is nothing to tell, and only this process's cache is reloaded.
///     </para>
///     <para>
///         A reload that fails is logged and not thrown, and so is a tag that cannot be invalidated: the write it follows has committed,
///         and an exception from the save would report it as one that did not.
///     </para>
/// </remarks>
public sealed partial class LookupCacheReloadInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private const string TenantProperty = "TenantId";

    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, ILookupCacheLoader> _loaders;
    private readonly ICacheStack? _cacheStack;
    private readonly ILogger _logger;

    // What each context wrote to a lookup and has not yet committed. Keyed weakly, as RollUpInterceptor keys the transactions it owns,
    // so a context abandoned mid-transaction leaks nothing.
    private static readonly ConditionalWeakTable<DbContext, HashSet<(Type Entity, string? Tenant)>> Pending = new();

    /// <summary>
    ///     Creates the interceptor over the loaders the host registered, one per <c>[Lookup]</c> entity, and the cache stack the other nodes
    ///     hear the reload through, if the host has one.
    /// </summary>
    public LookupCacheReloadInterceptor(
        IServiceProvider serviceProvider,
        IEnumerable<ILookupCacheLoader> loaders,
        ILogger<LookupCacheReloadInterceptor>? logger = null,
        ICacheStack? cacheStack = null)
    {
        _serviceProvider = serviceProvider;
        _loaders = loaders.ToDictionary(l => l.EntityType);
        _cacheStack = cacheStack;
        _logger = logger ?? NullLogger<LookupCacheReloadInterceptor>.Instance;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context && context.Database.CurrentTransaction is null)
            ReloadPendingBlocking(context);
        return result;
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && context.Database.CurrentTransaction is null)
            await ReloadPending(context, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context && context.Database.CurrentTransaction is null)
            Pending.Remove(context);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    void IDbTransactionInterceptor.TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is { } context)
            ReloadPendingBlocking(context);
    }

    /// <inheritdoc />
    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
        => eventData.Context is { } context ? ReloadPending(context, cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    void IDbTransactionInterceptor.TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is { } context)
            Pending.Remove(context);
    }

    /// <inheritdoc />
    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        if (eventData.Context is { } context)
            Pending.Remove(context);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null || _loaders.Count == 0)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || !_loaders.ContainsKey(entry.Metadata.ClrType))
                continue;

            var tenant = entry.Metadata.FindProperty(TenantProperty) is null ? null : entry.Property(TenantProperty).CurrentValue as string;
            Pending.GetOrCreateValue(context).Add((entry.Metadata.ClrType, tenant));
        }
    }

    // A synchronous save waits for the loader, which is asynchronous: Task.Run detaches from an ambient SynchronizationContext before
    // blocking, as CachedPermissionResolver does for its providers.
    private void ReloadPendingBlocking(DbContext context)
        => Task.Run(() => ReloadPending(context, CancellationToken.None)).GetAwaiter().GetResult();

    private async Task ReloadPending(DbContext context, CancellationToken ct)
    {
        if (!Pending.TryGetValue(context, out var written))
            return;
        Pending.Remove(context);

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
