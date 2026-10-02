using Invoicing.Billing.Errors;

namespace Invoicing.Billing.Infrastructure.Jobs;

/// <summary>
///     Chases one company's overdue invoices. The company is the one the ambient tenant scope names.
/// </summary>
/// <remarks>
///     The unit the tests drive: the job above it only decides <em>which</em> tenants to run this for, and
///     a test that went through the scheduler would be asserting the polling interval and the lease as
///     well as the reminder.
/// </remarks>
public interface IOverdueReminderSweep
{
    /// <summary>
    ///     Sends one reminder per invoice that is overdue on <paramref name="today" /> and has not been
    ///     chased in the last week, and answers how many left.
    /// </summary>
    /// <returns>
    ///     The number of reminders sent, or <see cref="NoTenantResolvedError" /> when there is no tenant in
    ///     scope — which is a refusal and not a zero, because the two are indistinguishable otherwise and
    ///     only one of them means the sweep did its job.
    /// </returns>
    Task<Result<int, NoTenantResolvedError>> RunAsync(DateOnly today, CancellationToken ct = default);
}
