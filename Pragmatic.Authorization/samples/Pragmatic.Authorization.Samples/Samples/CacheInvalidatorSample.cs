using Pragmatic.Authorization.Evaluation;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Demonstrates <see cref="IPermissionCacheInvalidator"/>. When cross-request permission
///     caching is enabled (<c>AuthorizationBuilder.UsePermissionCache</c>), the resolved permission
///     set for a user is cached. After a user's roles/permissions change (or a tenant-wide change),
///     the application must invalidate the cache so the next check re-resolves from the stores.
///     <para>
///     The production implementation invalidates through the <c>ICacheStack</c> tags written by
///     <c>CachedPermissionResolver</c> (<c>user:{id}</c> / <c>tenant:{id}</c>). To keep this samples
///     project free of a caching-infrastructure dependency, this sample uses a tiny in-memory fake
///     that implements the same interface and tracks which entries were invalidated.
///     </para>
/// </summary>
public static class CacheInvalidatorSample
{
    /// <summary>In-memory fake that records invalidations instead of touching a real cache.</summary>
    private sealed class FakeCacheInvalidator(IEnumerable<string> users, IEnumerable<string> tenants)
        : IPermissionCacheInvalidator
    {
        private readonly HashSet<string> _cachedUsers = new(users);
        private readonly HashSet<string> _cachedTenants = new(tenants);

        public bool IsUserCached(string userId) => _cachedUsers.Contains(userId);
        public bool IsTenantCached(string tenantId) => _cachedTenants.Contains(tenantId);

        public ValueTask InvalidateUserAsync(string userId, CancellationToken ct = default)
        {
            _cachedUsers.Remove(userId);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateTenantAsync(string tenantId, CancellationToken ct = default)
        {
            _cachedTenants.Remove(tenantId);
            return ValueTask.CompletedTask;
        }

        private readonly HashSet<string> _invalidatedRoles = [];
        private readonly HashSet<string> _invalidatedGroups = [];

        public ValueTask InvalidateRoleAsync(string roleName, CancellationToken ct = default)
        {
            _invalidatedRoles.Add(roleName);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateGroupAsync(string groupName, CancellationToken ct = default)
        {
            _invalidatedGroups.Add(groupName);
            return ValueTask.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Permission Cache Invalidator Sample ---");

        var fake = new FakeCacheInvalidator(
            users: ["user-1", "user-2", "user-3"],
            tenants: ["tenant-a", "tenant-b"]);

        IPermissionCacheInvalidator invalidator = fake;

        Console.WriteLine($"user-1 cached at start:   {fake.IsUserCached("user-1")}");
        Console.WriteLine($"tenant-a cached at start: {fake.IsTenantCached("tenant-a")}");

        // A single user's roles changed -> invalidate just that user.
        await invalidator.InvalidateUserAsync("user-1");
        Console.WriteLine($"After InvalidateUserAsync('user-1'):     user-1 cached: {fake.IsUserCached("user-1")}");

        // A tenant-wide permission change -> invalidate the whole tenant.
        await invalidator.InvalidateTenantAsync("tenant-a");
        Console.WriteLine($"After InvalidateTenantAsync('tenant-a'): tenant-a cached: {fake.IsTenantCached("tenant-a")}");

        Console.WriteLine("Untouched entries remain cached: " +
            $"user-2={fake.IsUserCached("user-2")}, tenant-b={fake.IsTenantCached("tenant-b")}");

        Console.WriteLine("Permission cache invalidation complete.");
        Console.WriteLine();
    }
}
