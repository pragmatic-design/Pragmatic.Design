using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Entities;

/// <summary>
///     EF Core interceptor that captures domain events from tracked entities
///     and persists them as <see cref="OutboxMessage"/> in the SAME transaction.
/// </summary>
/// <remarks>
///     Uses <c>SavingChangesAsync</c> (PRE-commit) to write outbox messages atomically.
///     This replaces the in-process dispatch <c>EfCoreUnitOfWork</c> does after the commit:
///     the events are taken from the entities here, so there is nothing left for it to find.
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
            CollectAndPersistOutboxMessages(eventData.Context);

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            CollectAndPersistOutboxMessages(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void CollectAndPersistOutboxMessages(DbContext context)
    {
        var entitiesWithEvents = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (entitiesWithEvents.Count == 0)
            return;

        var allEvents = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        LogEventsCollected(allEvents.Count, entitiesWithEvents.Count);

        // Resolve the shared JSON options from the seam (AOT-safe contexts when configured),
        // falling back to the reflection-based default for a manually-constructed DbContext.
        var serializerOptions = ResolveSerializerOptions(context);

        // Capture the ambient request context ONCE per SaveChanges so every outbox row
        // carries the originating tenant/user/correlation — without it, OutboxDeliveryService
        // reconstructs a MessageContext with null tenant/user and multi-tenant + tracing break.
        var ambient = CaptureAmbientContext(context, serializerOptions);

        // Persist each event as an outbox message
        foreach (var domainEvent in allEvents)
        {
            var eventType = domainEvent.GetType();
            var outboxMessage = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = eventType.FullName ?? eventType.Name,
                // Serialize through the resolver's JsonTypeInfo (AOT-safe: the SG-generated
                // JsonSerializerContext carries every domain-event type). Symmetric with the read
                // path, which deserializes via the SG IMessageTypeRegistry. No reflection fallback.
                Payload = JsonSerializer.Serialize(domainEvent, serializerOptions.GetTypeInfo(eventType)),
                CreatedAt = DateTimeOffset.UtcNow,
                RetryCount = 0,
                CorrelationId = ambient.CorrelationId,
                TenantId = ambient.TenantId,
                UserId = ambient.UserId,
                HeadersJson = ambient.HeadersJson
            };

            context.Set<OutboxMessage>().Add(outboxMessage);
        }

        // Clear events from entities to prevent re-dispatch
        foreach (var entity in entitiesWithEvents)
            entity.ClearDomainEvents();

        LogOutboxMessagesPersisted(allEvents.Count);
    }

    /// <summary>
    ///     Captures tenant / user / correlation from the current request scope.
    ///     The interceptor is stored in singleton <c>DbContextOptions</c>, so scoped services
    ///     are resolved per-call from the DbContext's application service provider rather than
    ///     constructor-injected. Correlation falls back to the ambient <see cref="Activity"/>.
    /// </summary>
    private static AmbientContext CaptureAmbientContext(DbContext context, JsonSerializerOptions serializerOptions)
    {
        // The application's scoped IServiceProvider — present when the DbContext is registered via
        // AddDbContext. Null for a manually-constructed context (tests).
        //
        // ⚠️ Through CoreOptionsExtension.ApplicationServiceProvider, and not
        // `GetInfrastructure().GetService<IServiceProvider>()`: that one is EF's **internal** provider,
        // where no application service is registered, so tenant, user and JSON options all resolved to
        // null and every row was written without them. It failed silently — a null tenant is
        // a valid single-tenant row — and every existing test of this interceptor built its context by
        // hand, where there is no application provider to get wrong.
        var services = ApplicationServicesOf(context);

        var currentUser = services?.GetService<ICurrentUser>();
        var tenant = services?.GetService<ITenantContext>();

        var activity = Activity.Current;
        var correlationId = activity?.GetBaggageItem("correlation.id")
            ?? activity?.Id
            ?? activity?.TraceId.ToString();

        // Tenant precedence: explicit tenant context → user's tenant claim.
        var tenantId = tenant?.TenantId ?? currentUser?.TenantId;

        var headersJson = BuildHeadersJson(activity, serializerOptions);

        return new AmbientContext(correlationId, tenantId, currentUser?.IdOrNull(), headersJson);
    }

    private static JsonSerializerOptions ResolveSerializerOptions(DbContext context)
    {
        var jsonOptions = ApplicationServicesOf(context)?.GetService<PragmaticJsonOptions>();
        return (jsonOptions ?? PragmaticJsonOptions.Default).Build();
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
    ///     one of them without an error.
    /// </remarks>
    private static IServiceProvider? ApplicationServicesOf(DbContext context)
        => context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;

    [UnconditionalSuppressMessage("AOT", "IL2026", Justification = "Header bag is a dictionary of strings; AOT apps use SG-generated TypeRegistry for the message payload.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Header bag is a dictionary of strings; AOT apps use SG-generated TypeRegistry for the message payload.")]
    private static string? BuildHeadersJson(Activity? activity, JsonSerializerOptions serializerOptions)
    {
        if (activity is null)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in activity.Baggage)
        {
            if (value is not null)
                headers[key] = value;
        }

        if (activity.TraceId != default)
            headers["traceparent"] = activity.Id ?? activity.TraceId.ToString();

        return headers.Count == 0 ? null : JsonSerializer.Serialize(headers, serializerOptions);
    }

    private readonly record struct AmbientContext(
        string? CorrelationId,
        string? TenantId,
        string? UserId,
        string? HeadersJson);

    // =========================================================================
    // LoggerMessage - Zero Allocation Logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Collected {EventCount} domain event(s) from {EntityCount} entit(ies)")]
    private partial void LogEventsCollected(int eventCount, int entityCount);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Persisted {Count} outbox message(s) to be delivered")]
    private partial void LogOutboxMessagesPersisted(int count);
}
