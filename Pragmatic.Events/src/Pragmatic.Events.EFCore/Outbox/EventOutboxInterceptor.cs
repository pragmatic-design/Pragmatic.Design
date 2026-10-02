using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Serialization;

namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     <see cref="SaveChangesInterceptor"/> that captures domain events into the
///     <see cref="EventOutboxEntry"/> table <em>before</em> the changes are written —
///     so the outbox rows are committed in the same transaction as the entity changes.
/// </summary>
/// <remarks>
///     This is the transactional outbox pattern: an event can never be lost relative to
///     a committed change, and a rolled-back change never emits an event. Events are
///     cleared from the entities here, so the post-commit dispatch in <c>EfCoreUnitOfWork</c>
///     finds nothing left to take — the two are safe to combine, and the outbox wins.
/// </remarks>
public sealed class EventOutboxInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            CaptureEvents(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            CaptureEvents(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void CaptureEvents(DbContext context)
    {
        var entities = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (entities.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;

        // Capture the ambient tenant ONCE per SaveChanges. The interceptor is a singleton stored
        // in DbContextOptions, so the scoped ITenantContext is resolved from the DbContext's
        // application service provider rather than constructor-injected. Persisting it lets the
        // delivery loop restore the originating tenant before invoking handlers.
        var tenantId = ResolveTenantId(context);

        // Capture the ambient distributed-trace id once so async delivery re-attaches to it.
        var traceParent = System.Diagnostics.Activity.Current?.Id;

        // Serialize through the SG-generated JsonSerializerContext (AOT-safe, no reflection): the
        // JsonTypeInfo overloads avoid IL2026/IL3050. Falls back to reflection metadata when the app
        // has not opted into JSON context generation.
        var serializerOptions = ResolveSerializerOptions(context);

        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                var eventType = domainEvent.GetType();
                context.Add(new EventOutboxEntry
                {
                    Id = Guid.NewGuid(),
                    EventType = eventType.AssemblyQualifiedName ?? eventType.FullName ?? eventType.Name,
                    Payload = JsonSerializer.Serialize(domainEvent, serializerOptions.GetTypeInfo(eventType)),
                    OccurredAt = domainEvent.OccurredAt,
                    CreatedAt = now,
                    TenantId = tenantId,
                    TraceParent = traceParent,
                });
            }

            // Cleared so EfCoreUnitOfWork finds nothing to take after the commit and the same event
            // is not delivered twice — once by the outbox and once in-process.
            entity.ClearDomainEvents();
        }
    }

    private static JsonSerializerOptions ResolveSerializerOptions(DbContext context)
    {
        // Falls back to the shared default when the context was built without an application
        // provider (a manually constructed context, as in unit tests).
        return (ApplicationServices(context)?.GetService<PragmaticJsonOptions>()
                ?? PragmaticJsonOptions.Default).Build();
    }

    private static string? ResolveTenantId(DbContext context)
        => ApplicationServices(context)?.GetService<ITenantContext>()?.TenantId;

    /// <summary>
    ///     The <em>application's</em> service provider, which is where the scoped services this
    ///     interceptor needs actually live.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Not <c>context.GetInfrastructure()</c>. That is EF's <b>internal</b> provider, and
    ///         resolving <c>IServiceProvider</c> from it hands back the internal one again — so every
    ///         lookup for an application service through it answers null. Both lookups here did, and
    ///         both had a fallback that made it invisible: the tenant became "no tenant" and the JSON
    ///         options became the shared default. The first meant an event delivered outside a request
    ///         restored no tenant, so every query in its handler read zero rows and the handler
    ///         reported success on having done nothing.
    ///     </para>
    ///     <para>
    ///         <c>CoreOptionsExtension.ApplicationServiceProvider</c> is the scoped provider that
    ///         registered the context, which is what <c>AddDbContext</c> puts there. Null for a
    ///         context constructed by hand, and that is the only case the fallbacks above are for.
    ///     </para>
    /// </remarks>
    private static IServiceProvider? ApplicationServices(DbContext context)
        => context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
}
