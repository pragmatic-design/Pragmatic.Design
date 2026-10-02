using Microsoft.Extensions.Logging;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.MultiTenancy;
using Pragmatic.Resilience.Attributes;
using Pragmatic.Temporal.Clock;

namespace Invoicing.Billing.Infrastructure.Jobs;

/// <summary>
///     Every morning at seven, every company's overdue invoices are chased.
/// </summary>
/// <remarks>
///     <para>
///         The job itself is tenant-independent, and that is the only case in which the declared form is
///         correct: <c>[RecurringJob]</c> takes a cron expression, an id and a time zone — it cannot carry
///         a tenant, and each run is enqueued with the definition's null one. So this fans out: it asks
///         which companies are active, opens a scope for each, and hands the work to the sweep.
///     </para>
///     <para>
///         ⚠️ Without the scope every query inside would read <b>zero rows</b> — not an error, not an empty
///         database: the tenant filter is fail-closed, so a job that simply queried would report success
///         over nothing and chase nobody, for ever.
///     </para>
///     <para>
///         Asking <c>ITenantStore</c> for the list works here because the store is this application's own
///         (<c>OrganizationTenantStore</c>, over the companies it onboarded). The warning against
///         enumerating it is about the framework's stock in-memory one, which nothing ever writes to.
///     </para>
/// </remarks>
[RecurringJob("0 7 * * *", TimeZone = "Europe/Rome")]
[Timeout(TimeoutSeconds = 600)]
public sealed partial class ChaseOverdueInvoicesJob(
    ITenantStore tenants,
    IOverdueReminderSweep sweep,
    IClock clock,
    ILogger<ChaseOverdueInvoicesJob> logger) : IJob
{
    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var today = clock.UtcToday;
        var active = await tenants.GetActiveAsync(ct).ConfigureAwait(false);

        var reminded = 0;
        foreach (var company in active)
        {
            using var scope = TenantScope.BeginScope(company.TenantId);

            var swept = await sweep.RunAsync(today, ct).ConfigureAwait(false);
            if (swept.IsFailure)
            {
                // One company's failure is not the sweep's: the others still get chased.
                LogTenantFailed(company.TenantId, swept.Error.Code);
                continue;
            }

            reminded += swept.Value;
        }

        LogFannedOut(active.Count, reminded);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Chased the overdue invoices of {Tenants} compan(ies): {Reminded} reminder(s) sent.")]
    private partial void LogFannedOut(int tenants, int reminded);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tenant {TenantId} was not swept: {Error}")]
    private partial void LogTenantFailed(string tenantId, string error);
}
