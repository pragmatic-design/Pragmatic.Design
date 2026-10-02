using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pragmatic.Identity.Keycloak;

/// <summary>
///     The Keycloak interaction surface a provider-specific package adds on top of generic OIDC: provisioning
///     users and synchronising realm roles via the admin REST API. This is what lets the app drive the IdP
///     (e.g. just-in-time provisioning, role sync) rather than only validate its tokens.
/// </summary>
/// <remarks>
///     A user who can sign in takes four steps beyond creating it: its attributes (<see cref="KeycloakUser.Attributes" />,
///     for the claims a realm maps from them), a credential (<see cref="SetPasswordAsync" />), the realm roles
///     it should hold (<see cref="EnsureRealmRoleAsync" /> for a role the application defines at run time, then
///     <see cref="AssignRealmRoleAsync" />), and removing the ones Keycloak grants every new user
///     (<see cref="RemoveRealmRoleAsync" /> with <c>default-roles-{realm}</c>).
/// </remarks>
public interface IKeycloakAdminClient
{
    /// <summary>Creates a realm user and returns its Keycloak id.</summary>
    Task<string> CreateUserAsync(KeycloakUser user, CancellationToken ct = default);

    /// <summary>Returns whether a user with the given username exists in the realm.</summary>
    Task<bool> UserExistsAsync(string username, CancellationToken ct = default);

    /// <summary>Assigns a realm role to a user (idempotent on the Keycloak side). The role must exist.</summary>
    Task AssignRealmRoleAsync(string userId, string roleName, CancellationToken ct = default);

    /// <summary>Sets the user's password: until it has a credential, a provisioned user cannot sign in.</summary>
    /// <param name="userId">The Keycloak id <see cref="CreateUserAsync" /> returned.</param>
    /// <param name="password">The password.</param>
    /// <param name="temporary">Whether the user has to change it at the next sign-in.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetPasswordAsync(string userId, string password, bool temporary = false, CancellationToken ct = default);

    /// <summary>Creates the realm role unless the realm already has it.</summary>
    /// <remarks>
    ///     <see cref="AssignRealmRoleAsync" /> refuses a role the realm does not have, and an application
    ///     that defines its roles at run time has no other way to provision them.
    /// </remarks>
    Task EnsureRealmRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>Removes a realm role from the user's role mappings.</summary>
    /// <remarks>
    ///     Keycloak gives every new user <c>default-roles-{realm}</c>, a composite of
    ///     <c>offline_access</c> and <c>uma_authorization</c>, and those ride on every token as roles
    ///     nobody assigned. Removing that mapping is how a provisioned user's token carries only the roles
    ///     the application gave it.
    /// </remarks>
    Task RemoveRealmRoleAsync(string userId, string roleName, CancellationToken ct = default);
}

/// <summary>A user to provision in Keycloak.</summary>
/// <param name="Username">The username.</param>
/// <param name="Email">The e-mail address.</param>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name.</param>
/// <param name="Enabled">Whether the user can sign in once it has a credential.</param>
/// <param name="Attributes">
///     The user's attributes, each a list of values as Keycloak stores them — a tenant id, an external
///     key, anything a realm's protocol mapper turns into a claim. Null sends none.
/// </param>
public sealed record KeycloakUser(
    string Username,
    string? Email = null,
    string? FirstName = null,
    string? LastName = null,
    bool Enabled = true,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Attributes = null);
