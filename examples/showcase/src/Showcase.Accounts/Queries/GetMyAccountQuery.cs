using Showcase.Accounts.Dtos;
using Showcase.Accounts.Entities;

namespace Showcase.Accounts.Queries;

/// <summary>
///     The signed-in user's own account.
/// </summary>
/// <remarks>
///     The filter is the caller: the invoker fills <see cref="Id" /> from the <see cref="AppUser" /> the generated resolver finds for the
///     request's identity, so no id travels on the request. On every engine the resolver reads through the boundary's repository (#549).
/// </remarks>
[Query<AppUser, AppUserDto>(Single = true)]
[RequirePermission(AccountsPermissions.AppUser.Read)]
[Endpoint(HttpVerb.Get, "api/accounts/me")]
public partial class GetMyAccountQuery
{
    [FromCurrentUser(nameof(AppUser.Id))]
    public Guid Id { get; private set; }
}
