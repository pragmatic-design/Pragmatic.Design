using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events;
using Pragmatic.Identity;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.EFCore.Outbox;

/// <summary>
///     EF Core interceptor that captures domain events from tracked entities
///     and persists them as <see cref="OutboxMessage"/> in the SAME transaction.
/// </summary>
/// <remarks>
///     Uses <c>SavingChangesAsync</c> (PRE-commit) to write outbox messages atomically.
///     This replaces the in-process dispatch <c>EfCoreUnitOfWork</c> does after the commit:
///     the events are taken from the entities here, so there is nothing left for it to find.
///     What a message carries is <see cref="OutboxMessageCapture"/>'s, shared with the engines' save phases.
/// </remarks>
public sealed partial class OutboxInterceptor(ILogger<OutboxInterceptor>? logger = null) : SaveChangesInterceptor
{
    private readonly ILogger<OutboxInterceptor> _logger = logger ?? NullLogger<OutboxInterceptor>.Instance;

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            CollectAndPersistOutboxMessages(eventData);

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            CollectAndPersistOutboxMessages(eventData);

        return base.SavingChanges(eventData, result);
    }

    private void CollectAndPersistOutboxMessages(DbContextEventData eventData)
    {
        var context = eventData.Context!;

        // The interceptor is stored in singleton DbContextOptions, so the scoped services of this save (tenant, user, JSON options) are
        // resolved per call from the DbContext's application service provider rather than constructor-injected.
        var services = ApplicationServicesOf(context);

        var messages = OutboxMessageCapture.Take(
            SaveEntries.Of<IHasDomainEvents>(eventData).Select(e => (object)e.Entity),
            DateTimeOffset.UtcNow,
            services?.GetService<ITenantContext>(),
            services?.GetService<ICurrentUser>(),
            services?.GetService<PragmaticJsonOptions>());

        if (messages.Count == 0)
            return;

        context.Set<OutboxMessage>().AddRange(messages);

        LogOutboxMessagesPersisted(messages.Count);
    }

    /// <summary>
    ///     The application's service provider, as EF Core holds it — or <see langword="null" /> for a
    ///     context whose options were built by hand.
    /// </summary>
    /// <remarks>
    ///     <c>CoreOptionsExtension.ApplicationServiceProvider</c> is the one EF sets from the container
    ///     the context was registered in, and with <c>AddDbContext</c> that is the <b>scoped</b> provider
    ///     of the request — which is what makes the tenant and the user of this SaveChanges reachable
    ///     from a singleton interceptor. ⚠️ <c>GetInfrastructure()</c> is a different provider: EF's own
    ///     internal one, which knows nothing of the application's services and answers null for every
    ///     one of them without an error. Through it, tenant, user and JSON options all resolved to null
    ///     and every row was written without them. It failed silently — a null tenant is a valid
    ///     single-tenant row — and every existing test of this interceptor built its context by hand,
    ///     where there is no application provider to get wrong.
    /// </remarks>
    private static IServiceProvider? ApplicationServicesOf(DbContext context)
        => context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Persisted {Count} outbox message(s) to be delivered")]
    private partial void LogOutboxMessagesPersisted(int count);
}
