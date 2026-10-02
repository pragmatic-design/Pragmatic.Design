using Pragmatic.Authorization;

// The permissions of the RBAC management package. The assembly declares no boundary, so the first segment
// names the class: AuthorizationPermissions.Permissions.Manage, AuthorizationPermissions.View, …

// Managing permissions: a meta-permission — whoever holds it defines what the others can hold.
[assembly: Permission("authorization.permissions.manage", "Create, update, and delete dynamic permissions", Category = "Authorization")]

[assembly: Permission("authorization.roles.manage", "Create, update, and delete roles and their permission assignments", Category = "Authorization")]

[assembly: Permission("authorization.assignments.manage", "Assign and revoke roles and groups for users", Category = "Authorization")]

[assembly: Permission("authorization.view", "View permissions, roles, groups, and assignments (read-only)", Category = "Authorization")]
