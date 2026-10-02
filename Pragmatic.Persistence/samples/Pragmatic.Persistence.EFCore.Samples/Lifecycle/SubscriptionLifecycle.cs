using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.EFCore.Samples.Lifecycle;

/// <summary>
///     Lifecycle hooks for <see cref="Subscription"/>. Implements <see cref="IEntityLifecycle{T}"/>:
///     <c>OnCreating</c> runs before validation (set defaults), <c>OnSaving</c> runs after
///     validation (last-chance adjustments). The host invokes these around the mutation pipeline.
/// </summary>
public sealed class SubscriptionLifecycle : IEntityLifecycle<Subscription>
{
    public void OnCreating(Subscription entity, LifecycleContext context)
    {
        // Default an unset price based on the plan name.
        if (entity.MonthlyPrice == 0m)
        {
            entity.MonthlyPrice = entity.PlanName switch
            {
                "Pro" => 49.00m,
                "Team" => 99.00m,
                _ => 9.00m
            };
        }

        entity.CreatedAt = context.Now;
        entity.CreatedBy = context.UserId ?? "system";
    }

    public void OnSaving(Subscription entity, LifecycleContext context)
    {
        // Always have a renewal date one month out from creation.
        entity.RenewsOn ??= context.Now.AddMonths(1);
    }
}
