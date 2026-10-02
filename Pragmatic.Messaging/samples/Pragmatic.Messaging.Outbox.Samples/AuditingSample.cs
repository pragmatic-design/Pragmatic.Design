using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging;
using Pragmatic.Audit;
using Pragmatic.Messaging.Auditing;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     Message auditing: <c>AddPragmaticMessaging(b =&gt; b.EnableAuditing())</c>
///     registers <see cref="AuditMiddleware" /> (Order -100, outermost) plus an
///     the framework audit trail. Every dispatched message yields an
///     <see cref="AuditEntry" /> stamped with the context's correlation,
///     tenant, and user. Entries are retrieved with a filtered
///     <see cref="AuditQuery" />.
/// </summary>
public static class AuditingSample
{
    public sealed record AccountOpened(Guid AccountId, string Currency);

    public sealed class AccountOpenedHandler : IMessageHandler<AccountOpened>
    {
        public Task HandleAsync(AccountOpened message, MessageContext context, CancellationToken ct)
            => Task.CompletedTask;
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Message auditing (audit middleware + query) ---");

        var services = new ServiceCollection();
        services.AddLogging();
        // EnableAuditing wires AuditMiddleware (Order -100). The trail itself is the framework's:
        // in a real application that is AddAuditTrail() over a database. Here it is collected in
        // memory so the sample stays runnable without one.
        services.AddSingleton<IAuditTrail, CollectingTrail>();
        services.AddPragmaticMessaging(b => b.EnableAuditing());
        services.AddSingleton<IMessageHandler<AccountOpened>, AccountOpenedHandler>();

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var audit = (CollectingTrail)scope.ServiceProvider.GetRequiredService<IAuditTrail>();

        await bus.PublishAsync(new AccountOpened(Guid.NewGuid(), "EUR"),
            MessageContext.New(correlationId: "sess-1", tenantId: "acme", userId: "u-1"));
        await bus.PublishAsync(new AccountOpened(Guid.NewGuid(), "USD"),
            MessageContext.New(correlationId: "sess-1", tenantId: "globex", userId: "u-2"));

        var allEntries = audit.Entries;
        var acmeEntries = allEntries.FindAll(e => e.TenantId == "acme");

        Console.WriteLine($"  total audit entries      : {allEntries.Count} (expected 2)");
        Console.WriteLine($"  filtered tenant=acme     : {acmeEntries.Count} (expected 1)");
        foreach (var e in allEntries)
            Console.WriteLine(
                $"    - {e.TargetType?.Split('.')[^1]} corr={e.CorrelationId} tenant={e.TenantId} outcome={e.Outcome}");
        Console.WriteLine();
    }

    /// <summary>Stands in for a real trail, so the sample needs no database.</summary>
    private sealed class CollectingTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return default;
        }
    }
}
