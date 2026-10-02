using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Demonstrates the runtime-editable authorization flow exposed by the
///     <c>Pragmatic.Authorization.Management</c> package (CreateRole / AssignPermissionsToRole /
///     AssignRoleToUser / GetEffectivePermissions).
///     <para>
///     The Management package persists <c>DynamicRole</c>, <c>DynamicPermission</c>,
///     <c>DynamicRolePermission</c> and <c>UserRoleAssignment</c> through EF Core, so its actions
///     require a live <c>DbContext</c>. To keep this samples project self-contained (it references
///     only the runtime <c>Pragmatic.Authorization</c> assembly, not EF Core), this sample
///     reproduces the same management flow against in-memory stores and documents the real
///     EF-backed action API in comments. See <c>src/Pragmatic.Authorization.Management/Actions/</c>.
///     </para>
/// </summary>
public static class ManagementSample
{
    /// <summary>
    ///     In-memory stand-in for the EF-backed user-to-role assignment store
    ///     (the persisted entity is <c>UserRoleAssignment</c>).
    /// </summary>
    private sealed class InMemoryUserRoleStore
    {
        private readonly Dictionary<string, HashSet<string>> _userRoles = new(StringComparer.OrdinalIgnoreCase);

        public bool Assign(string userId, string roleName)
        {
            if (!_userRoles.TryGetValue(userId, out var roles))
            {
                roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _userRoles[userId] = roles;
            }

            // Mirrors the duplicate-assignment guard enforced by the EF AssignRoleToUser action.
            return roles.Add(roleName);
        }

        public IReadOnlyCollection<string> RolesFor(string userId)
            => _userRoles.TryGetValue(userId, out var roles) ? roles : [];
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("--- Management Sample ---");

        // --- 1. Create roles + assign permissions ---
        // Real package:  await new CreateRole(dbContext) { Name = "editor" }.Execute(ct);
        //                await new AssignPermissionsToRole(dbContext) { ... }.Execute(ct);
        //                (each action calls dbContext.SaveChangesAsync to persist the DynamicRole /
        //                 DynamicRolePermission rows.)
        var roleStore = new InMemoryRolePermissionStore();
        roleStore.AddRole("editor", ["articles.read", "articles.write"]);
        roleStore.AddRole("viewer", ["articles.read"]);
        Console.WriteLine("Created roles: editor, viewer");

        // --- 2. Assign roles to a user ---
        // Real package:  await new AssignRoleToUser(dbContext) { UserId = "user-1", RoleName = "editor" }.Execute(ct);
        var userRoles = new InMemoryUserRoleStore();
        Console.WriteLine($"Assign editor to user-1:       {userRoles.Assign("user-1", "editor")}");
        Console.WriteLine($"Assign editor to user-1 again: {userRoles.Assign("user-1", "editor")} (duplicate guarded)");
        Console.WriteLine($"Assign viewer to user-1:       {userRoles.Assign("user-1", "viewer")}");

        // --- 3. Compute effective permissions for the user ---
        // Real package:  await new GetEffectivePermissions(dbContext, roleStore) { UserId = "user-1" }.Execute(ct);
        //                (unions GetPermissionsForRoleAsync over each active assigned role.)
        var effective = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in userRoles.RolesFor("user-1"))
        {
            var perms = await roleStore.GetPermissionsForRoleAsync(role);
            effective.UnionWith(perms);
        }

        Console.WriteLine("Effective permissions for user-1:");
        foreach (var permission in effective.OrderBy(p => p))
            Console.WriteLine($"  - {permission}");

        Console.WriteLine("Management flow complete.");
        Console.WriteLine();
    }
}
