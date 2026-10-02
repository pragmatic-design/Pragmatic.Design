using Pragmatic.Authorization.Providers;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Demonstrates implementing a custom <see cref="IPermissionProvider"/> and composing it with
///     the built-in providers via <see cref="CompositePermissionProvider"/>.
///     <para>
///     In a host, a custom provider is registered through the authorization builder
///     (<c>AddPermissionProvider&lt;T&gt;()</c>); the runtime then wraps every registered provider
///     in a <see cref="CompositePermissionProvider"/> that unions their results in <c>Order</c>.
///     This sample wires the composition directly so it runs without a DI container.
///     </para>
/// </summary>
public static class CustomPermissionProviderSample
{
    /// <summary>
    ///     Custom provider that grants permissions based on a "plan" claim
    ///     (e.g. a SaaS subscription tier mapped to feature permissions). Runs late (Order = 500).
    /// </summary>
    private sealed class SubscriptionPlanProvider : IPermissionProvider
    {
        private static readonly Dictionary<string, string[]> PlanPermissions = new()
        {
            ["free"] = ["reports.view"],
            ["pro"] = ["reports.view", "reports.export", "dashboards.create"],
        };

        public int Order => 500;

        public ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
            ICurrentUser user, CancellationToken ct = default)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (user.Claims.TryGetValue("plan", out var plans))
            {
                foreach (var plan in plans)
                {
                    if (PlanPermissions.TryGetValue(plan, out var perms))
                        result.UnionWith(perms);
                }
            }

            return ValueTask.FromResult<IReadOnlySet<string>>(result);
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Custom Permission Provider Sample ---");

        // Compose the built-in claims provider (Order 0) with the custom subscription provider.
        IEnumerable<IPermissionProvider> providers =
        [
            new ClaimsPermissionProvider(),
            new SubscriptionPlanProvider(),
        ];
        var composite = new CompositePermissionProvider(providers);

        var user = new SampleUser(
            id: "user-123",
            displayName: "Subscriber",
            claims: new Dictionary<string, IReadOnlyList<string>>
            {
                // Picked up by the built-in ClaimsPermissionProvider ("permission" claim).
                ["permission"] = ["users.read", "users.write"],
                // Picked up by the custom SubscriptionPlanProvider ("plan" claim).
                ["plan"] = ["pro"],
            });

        var permissions = await composite.ResolvePermissionsAsync(user);

        Console.WriteLine("Effective permissions (claims provider + subscription provider):");
        foreach (var permission in permissions.OrderBy(p => p))
            Console.WriteLine($"  - {permission}");

        Console.WriteLine($"Has 'reports.export' (from plan):  {permissions.Contains("reports.export")}");
        Console.WriteLine($"Has 'users.write' (from claims):   {permissions.Contains("users.write")}");

        Console.WriteLine("Custom permission provider complete.");
        Console.WriteLine();
    }
}
