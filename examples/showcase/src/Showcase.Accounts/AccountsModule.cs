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
public sealed class AccountsModule;
