// The permissions of this boundary that are not an entity's CRUD. Each becomes a constant of
// RegistryPermissions, beside the generated CRUD ones, and an entry of the permission catalogue.

// Creating the tenant itself. It runs before any tenant exists, which is why the action that carries it is
// [TenantAgnostic] — and why onboarding is not "create an organization row" to anyone but a platform
// administrator.
[assembly: Permission("registry.organization.onboard", "Onboard a company as a tenant of the service", Category = "Registry")]

// Suspending a tenant: its tokens stop being served, its rows stay.
[assembly: Permission("registry.organization.suspend", "Suspend a tenant, and let it be reactivated", Category = "Registry")]

// Seeing who one is and what one may do. Whose access is not in the permission: the operation reads the
// caller's own token and nothing else, so holding it never reaches another person's session.
[assembly: Permission("registry.own-access.read", "See who one is signed in as, and what one may do", Category = "Registry")]
