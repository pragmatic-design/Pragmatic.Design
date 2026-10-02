using Pragmatic.Logging.Privacy;
using Pragmatic.Logging.Privacy.Audit;
using Pragmatic.Logging.Privacy.Audit.Storage;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     The audit trail records compliance-relevant events (data redactions, sensitive-data
///     access, compliance violations) into a pluggable audit storage backend. This sample
///     uses <see cref="MemoryAuditStorage"/> — which also implements <see cref="IAuditQuery"/> —
///     so it runs entirely in-process: it stores a batch of <see cref="AuditEntry"/> records,
///     queries them back with <see cref="AuditQueryBuilder"/>, and prints aggregate statistics.
/// </summary>
public static class AuditTrailSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("\n--- Audit trail (storage + query + statistics) ---");

        using var storage = new MemoryAuditStorage();
        var now = DateTime.UtcNow;

        // Persist a representative batch of audit events.
        await storage.StoreEntriesAsync(
        [
            new AuditEntry
            {
                Timestamp = now.AddMinutes(-10),
                EventType = AuditEventType.DataRedaction,
                CategoryName = "Billing.Invoices",
                PropertyName = "CustomerEmail",
                RedactionReason = RedactionReason.ComplianceRequirement,
                ComplianceStandard = ComplianceStandard.Gdpr,
                Severity = AuditSeverity.Medium,
                UserId = "user-17",
                CorrelationId = "corr-abc",
            },
            new AuditEntry
            {
                Timestamp = now.AddMinutes(-5),
                EventType = AuditEventType.SensitiveDataAccess,
                DataType = "PaymentCard",
                AccessReason = "Refund processing",
                ComplianceStandard = ComplianceStandard.PciDss,
                Severity = AuditSeverity.High,
                UserId = "user-17",
                CorrelationId = "corr-abc",
            },
            new AuditEntry
            {
                Timestamp = now.AddMinutes(-1),
                EventType = AuditEventType.ComplianceViolation,
                ViolationType = "UnmaskedPiiInLog",
                Description = "Email written to log without redaction",
                ComplianceStandard = ComplianceStandard.Gdpr,
                Severity = AuditSeverity.Critical,
                UserId = "user-42",
                CorrelationId = "corr-xyz",
            },
        ]);

        // Read entries back for the last hour (from inclusive, until exclusive).
        var entries = await storage.GetEntriesAsync(now.AddHours(-1), now.AddMinutes(1));
        Console.WriteLine($"Stored {entries.Count} audit entries in the last hour:");
        foreach (var entry in entries)
        {
            Console.WriteLine(
                $"  [{entry.Severity,-8}] {entry.EventType,-20} std={entry.ComplianceStandard,-7} user={entry.UserId}");
        }

        // Query via the fluent builder (filters/sort/paging are expressed declaratively).
        var query = new AuditQueryBuilder()
            .WhereComplianceStandard(ComplianceStandard.Gdpr)
            .WhereSeverityAtLeast(AuditSeverity.Medium)
            .OrderByTimestamp()
            .Take(10);
        var result = await storage.QueryAsync(query);
        Console.WriteLine($"\nQuery returned {result.TotalCount} entries (HasMore={result.HasMore}).");

        // Aggregate statistics over the window.
        var stats = await storage.GetStatisticsAsync(now.AddHours(-1), now.AddMinutes(1));
        Console.WriteLine($"\nStatistics over {stats.CoverageTimeSpan.TotalMinutes:0} min — total {stats.TotalEntries}:");
        foreach (var (severity, count) in stats.EntriesBySeverity.OrderByDescending(p => p.Key))
        {
            Console.WriteLine($"  severity {severity}: {count}");
        }
        foreach (var (user, count) in stats.TopUsersByActivity)
        {
            Console.WriteLine($"  top user {user}: {count} events");
        }

        // Retention: delete everything older than 2 minutes.
        var deleted = await storage.DeleteEntriesAsync(now.AddMinutes(-2));
        var remaining = await storage.GetEntryCountAsync(now.AddHours(-1), now.AddMinutes(1));
        Console.WriteLine($"\nRetention purge removed {deleted} entries; {remaining} remain.");
    }
}
