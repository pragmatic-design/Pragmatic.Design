using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Lifecycle;

/// <summary>
///     Demonstrates the entity lifecycle building blocks the host wires around the mutation
///     pipeline: <c>IDefaultValueGenerator</c> (mirrors <c>[GeneratedValue]</c> format),
///     <c>IEntityLifecycle</c> (OnCreating/OnSaving), and <c>[HasPresets]</c> /
///     <c>IPresetProvider</c> child seeding. Here the hooks are invoked explicitly in the
///     order the runtime uses, then persisted via EF Core InMemory.
/// </summary>
public static class LifecycleSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Entity Lifecycle (DefaultValue + Lifecycle hooks + Presets + GeneratedValue) ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<LifecycleDbContext>()
            .UseInMemoryDatabase($"Lifecycle_{Guid.NewGuid():N}")
            .Options;

        await using var db = new LifecycleDbContext(options);

        var context = new Pragmatic.Persistence.Lifecycle.LifecycleContext
        {
            Now = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            UserId = "user:carol"
        };

        var numberGenerator = new SubscriptionNumberGenerator();
        var lifecycle = new SubscriptionLifecycle();
        var presetProvider = new SubscriptionPresetProvider();

        var subscription = new Subscription { PlanName = "Pro" };

        // 1. OnCreating — defaults (price from plan, audit stamps) run before validation.
        lifecycle.OnCreating(subscription, context);

        // 2. [GeneratedValue] / [ComputedDefault] → IDefaultValueGenerator produces the business key.
        subscription.SubscriptionNumber = await numberGenerator.GenerateAsync(subscription, context, default);

        // 3. OnSaving — last-chance adjustments after validation.
        lifecycle.OnSaving(subscription, context);

        // 4. [HasPresets] → IPresetProvider seeds child entities in the same unit of work.
        var presets = await presetProvider.CreatePresetsAsync(subscription, context, default);

        db.Subscriptions.Add(subscription);
        db.AddOns.AddRange(presets.Cast<SubscriptionAddOn>());
        await db.SaveChangesAsync();

        Console.WriteLine("  Subscription created:");
        Console.WriteLine($"    Number (GeneratedValue fmt) : {subscription.SubscriptionNumber}");
        Console.WriteLine($"    Plan                      : {subscription.PlanName}");
        Console.WriteLine($"    MonthlyPrice (OnCreating) : {subscription.MonthlyPrice:C}");
        Console.WriteLine($"    CreatedBy (OnCreating)    : {subscription.CreatedBy}");
        Console.WriteLine($"    RenewsOn (OnSaving)       : {subscription.RenewsOn:yyyy-MM-dd}");
        Console.WriteLine();

        var seeded = await db.AddOns.Where(a => a.SubscriptionId == subscription.Id).ToListAsync();
        Console.WriteLine($"  Preset add-ons seeded     : {seeded.Count} (Pro plan → 2)");
        foreach (var a in seeded)
            Console.WriteLine($"    - {a.Name,-18} {a.Price:C}");
        Console.WriteLine();
    }
}
