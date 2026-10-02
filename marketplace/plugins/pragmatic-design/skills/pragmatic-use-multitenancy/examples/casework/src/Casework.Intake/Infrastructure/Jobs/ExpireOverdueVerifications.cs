using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;

namespace Casework.Intake.Infrastructure.Jobs;

/// <summary>
///     The cases nobody answered in time, stopped waiting.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The read is deliberately across tenants and the writes are not.</b> A job has no
///         request and therefore no tenant, and the tenant filter is fail-closed — so a job that simply
///         queried would read <b>zero rows</b>, report success over nothing, and let cases wait for ever.
///         <c>FilterMode.Background</c> is the framework's declared answer — "background jobs that
///         operate across tenants but respect soft-delete" — for the <em>one</em> query that answers
///         <em>which organisations</em> have overdue cases; each organisation's cases are then read and
///         written again inside a scope of its own, so every write is stamped, routed and audited as that
///         tenant's.
///     </para>
///     <para>
///         ⚠️ <c>UseMode(Background)</c> and not <c>DisableAll()</c>, which reads <b>zero rows</b> here:
///         the tenant condition is an EF Core <em>named</em> query
///         filter as well as a provider filter, and the generated repository lifts it from
///         <c>FilterContext.SkipTenant</c> — which the mode sets and a blanket disable does not. The two
///         forms look interchangeable at the call site and are not.
///     </para>
///     <para>
///         ⚠️ <b>A scope per tenant, and a container scope with it.</b> A <c>TenantScope</c> around the
///         in-memory state change, with the save <em>outside</em> it on a unit of work constructed up
///         front, leaves the write with no tenant resolved: with a database per tenant it would land on
///         the <b>shared</b> one, and on a shared schema it would carry an empty tenant column. The
///         connection is chosen when a context opens it, so reusing one context across organisations
///         cannot be made right by ordering — each organisation needs a context of its own, which is what
///         a container scope gives. <c>TenantInterceptor</c> refuses such a write, so the mistake fails
///         loudly instead of landing in the wrong place.
///     </para>
///     <para>
///         The ordering is still the other way from the obvious one: the tenants come from the cases, not
///         from a register. Asking a tenant store for the active organisations — what Invoicing does — is
///         the shape to move to when this service's own register is the source of truth for sweeping too;
///         it costs one query per organisation instead of one per organisation <em>with work</em>.
///     </para>
///     <para>
///         ⚠️ <b>Why a job and not <c>[SagaTimeout]</c>.</b> They are different things and an application
///         can want both. A saga timeout is a <b>wall-clock duration from a step</b> — "if nothing has
///         happened in twenty minutes, give up on this conversation" — measured by the framework against
///         the instance's <c>TimeoutAt</c>. This deadline is a <b>date somebody agreed to</b>: it is on
///         the case, an operator can read it, the other service was told it in the request, and it has to
///         mean the same thing after a restart, a deployment, or a week with the consumer switched off.
///         A duration cannot express "the tenth of March"; a date cannot express "twenty minutes of
///         silence". Casework has the first kind, so it has a job.
///     </para>
///     <para>
///         Durable because <c>[EnableJobPersistence]</c> on the boundary puts <c>__Jobs</c> and
///         <c>__RecurringJobs</c> in this database: the schedule is a row, so a restart does not forget
///         it and two hosts cannot both run the same occurrence. A deadline that lives in memory is not
///         a deadline.
///     </para>
/// </remarks>
[Service<IExpireOverdueVerifications>]
public sealed partial class ExpireOverdueVerifications(
    IRepository<Case> cases,
    IQueryFilterToggle filters,
    IServiceScopeFactory scopes,
    ILogger<ExpireOverdueVerifications> logger) : IExpireOverdueVerifications
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        List<Case> overdue;
        using (filters.UseMode(FilterMode.Background))
        {
            overdue = await cases
                .FindAsync(CaseSpecifications.AwaitingAnAnswerDueBefore(now), ct)
                .ConfigureAwait(false);
        }

        var expired = 0;
        foreach (var tenantId in overdue.Select(@case => @case.TenantId).Distinct())
            expired += await ExpireForAsync(tenantId, now, ct).ConfigureAwait(false);

        LogSwept(overdue.Count, expired);

        return expired;
    }

    /// <summary>
    ///     Expires one organisation's overdue cases, inside that organisation.
    /// </summary>
    /// <remarks>
    ///     The cases are read again here, and that is the point rather than a cost: this read is the
    ///     ordinary tenant-filtered one, so a row that came back from the cross-tenant query above and
    ///     does not belong to this organisation cannot be written by mistake.
    /// </remarks>
    private async Task<int> ExpireForAsync(string tenantId, DateTimeOffset now, CancellationToken ct)
    {
        using var tenant = TenantScope.BeginScope(tenantId);
        using var scope = scopes.CreateScope();

        var owned = scope.ServiceProvider.GetRequiredService<IRepository<Case>>();
        var unitOfWork = scope.ServiceProvider
            .GetRequiredKeyedService<IUnitOfWork>(typeof(IntakeBoundary));

        var expired = 0;
        foreach (var @case in await owned
                     .FindAsync(CaseSpecifications.AwaitingAnAnswerDueBefore(now), ct)
                     .ConfigureAwait(false))
        {
            var stopped = @case.ExpireVerification();
            if (stopped.IsFailure)
            {
                // The state machine refused: the case was decided between the read and now. Nothing to
                // do, and nothing wrong — the answer won the race, which is the outcome anybody wants.
                LogRefused(@case.Id, stopped.Error.Code);
                continue;
            }

            expired++;
        }

        if (expired > 0)
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return expired;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Overdue} case(s) past their verification deadline, {Expired} expired.")]
    private partial void LogSwept(int overdue, int expired);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Case {CaseId} was not expired: {Error} — it was decided while the sweep was running.")]
    private partial void LogRefused(Guid caseId, string error);
}
