using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Identity.Local.Actions;
using Showcase.Accounts.Infrastructure.Endpoints;

namespace Showcase.Accounts;

/// <summary>
///     User account management module.
///     Imports the local identity provider — its actions (login, register, change password)
///     and services are fused into this module's metadata automatically by the SG.
///     Endpoints are promoted here — the consumer decides what is exposed via HTTP.
/// </summary>
[Module(Name = "Showcase.Accounts", Version = "1.0.0", Description = "User accounts and local identity")]
[UsePackage<LocalIdentityPackage, AccountsBoundary>]
[UsePackage<global::Pragmatic.Authorization.Management.AuthorizationManagementPackage, AccountsBoundary>]
// The second admin surface, next to authorization's: what configuration says. Its actions carry no
// [Endpoint] of their own — a package proposes operations and the consumer decides which become
// routes, which is the [ExposeEndpoint] below.
//
// ⚠️ The package's actions declare [BelongsTo<TPackage>], which is not a boundary: read as one, the
// invoker would ask for an IUnitOfWork keyed by the package and the host would stop at container
// validation.
[UsePackage<global::Pragmatic.Configuration.Management.ConfigurationManagementPackage, AccountsBoundary>]
[ExposeEndpoint<LoginUser>(HttpVerb.Post, "login")]
[ExposeEndpoint<RegisterUser>(HttpVerb.Post, "register")]
[ExposeEndpoint<ChangePassword>(HttpVerb.Post, "change-password")]
[ExposeEndpoint<RequestPasswordReset>(HttpVerb.Post, "reset-password/request")]
[ExposeEndpoint<ConfirmPasswordReset>(HttpVerb.Post, "reset-password/confirm")]
[ExposeEndpoint<RequestEmailVerification>(HttpVerb.Post, "verify-email/request")]
[ExposeEndpoint<ConfirmEmail>(HttpVerb.Post, "verify-email/confirm")]
// Read-only of the six: seeing what configuration resolves to is the half an operator needs first,
// and exposing the setter is a decision this example does not need to make to show the package.
// Published inside OperationsGroup, so at /api/operations/configuration/values.
[ExposeEndpoint<GetConfigValues, OperationsGroup>(HttpVerb.Get, "configuration/values")]
// Taking back what a role grants (#725), the half of role management an operator cannot do without a deploy otherwise: revoking a grant,
// and deactivating a role while keeping its rows. At /api/operations/roles/….
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Roles.RevokePermissionsFromRole, OperationsGroup>(HttpVerb.Post, "roles/revoke-permissions")]
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Roles.DeactivateRole, OperationsGroup>(HttpVerb.Post, "roles/deactivate")]
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Roles.ReactivateRole, OperationsGroup>(HttpVerb.Post, "roles/reactivate")]
// Groups (#726): a group is given roles, and its members hold them. At /api/operations/groups/….
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Groups.CreateGroup, OperationsGroup>(HttpVerb.Post, "groups")]
// Over GET, its optional init input (TenantId) is bound from the query string (#780).
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Groups.ListGroups, OperationsGroup>(HttpVerb.Get, "groups")]
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Groups.AssignRoleToGroup, OperationsGroup>(HttpVerb.Post, "groups/assign-role")]
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Groups.RevokeRoleFromGroup, OperationsGroup>(HttpVerb.Post, "groups/revoke-role")]
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Assignments.AssignUserToGroup, OperationsGroup>(HttpVerb.Post, "groups/add-member")]
[ExposeEndpoint<global::Pragmatic.Authorization.Management.Actions.Assignments.RemoveUserFromGroup, OperationsGroup>(HttpVerb.Post, "groups/remove-member")]
public sealed class AccountsModule;
