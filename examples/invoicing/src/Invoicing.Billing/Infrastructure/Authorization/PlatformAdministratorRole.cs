using Invoicing.Registry;

namespace Invoicing.Billing.Infrastructure.Authorization;

/// <summary>
///     Runs the service: onboards a company as a tenant and suspends one, on top of everything an accountant
///     may do.
/// </summary>
/// <remarks>
///     Onboarding is the one thing that happens outside any tenant, so this role is the only one whose
///     operations run before a tenant exists.
/// </remarks>
[Role("platform-administrator", "Onboards and suspends the companies that use the service")]
[IncludesRole<AccountantRole>]
[Grants(
    RegistryPermissions.Organization.Onboard,
    RegistryPermissions.Organization.Suspend)]
public sealed partial class PlatformAdministratorRole;
