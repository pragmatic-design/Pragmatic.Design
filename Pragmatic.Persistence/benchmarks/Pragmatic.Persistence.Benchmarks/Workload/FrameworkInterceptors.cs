using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Events.EFCore;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Persistence.EFCore.Auditing;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Persistence.EFCore.RollUp;
using Pragmatic.Persistence.RollUp;

namespace Pragmatic.Persistence.Benchmarks.Workload;

public static class FrameworkInterceptors
{
    /// <summary>
    ///     The interceptors, in the order <c>DbContextRegistrationTemplate</c> emits them for a boundary with
    ///     tenancy, events, audited entities and the event outbox, and a signed-in caller in a resolved tenant.
    /// </summary>
    /// <remarks>
    ///     The messaging outbox is left out: a boundary captures its events into one outbox, and the event
    ///     outbox stands for either. Every interceptor here reads the tracker on every save, whether or not
    ///     <see cref="Ticket" /> implements the interface it looks for, because enumerating is what detects.
    /// </remarks>
    public static IInterceptor[] AsAHostRegistersThem()
    {
        var user = new SignedInUser();
        var time = TimeProvider.System;
        return
        [
            new AuditingInterceptor(time, user),
            new SoftDeleteInterceptor(time, user),
            new OwnershipInterceptor(user),
            new ScopeInterceptor(user),
            new TenantInterceptor(new ResolvedTenant()),
            new RollUpInterceptor([TicketAmountsIntoProjectTotal()]),
            new LifecycleEventsInterceptor(),
            new AuditLogInterceptor(time, user),
            new EventOutboxInterceptor(),
        ];
    }

    private static RollUpRule<Ticket, Project> TicketAmountsIntoProjectTotal() => new()
    {
        AggregatePropertyName = nameof(Project.Total),
        ParentKey = t => t.ProjectId,
        Amount = t => t.Amount,
        ApplyToParent = (project, delta) => project.ApplyDelta(delta),
    };
}
