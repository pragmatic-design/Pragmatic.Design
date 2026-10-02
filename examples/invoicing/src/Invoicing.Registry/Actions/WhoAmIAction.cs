namespace Invoicing.Registry.Actions;

/// <summary>
///     Answers who the caller is signed in as, and what that lets them do.
/// </summary>
/// <remarks>
///     The first call a client makes, and the only operation in the application that reads the token instead
///     of the database: Invoicing issues no token and keeps no user row, so the identity is whatever the
///     company's provider put in it. It carries a permission of its own — every role holds it — so that an
///     anonymous call is 401 and a token with no role is 403, rather than an open endpoint that says who you
///     are before anything has decided you may ask.
/// </remarks>
[DomainAction]
[RequirePermission(RegistryPermissions.OwnAccess.Read)]
[Endpoint(HttpVerb.Get, "api/me")]
public partial class WhoAmIAction : DomainAction<CallerDto>
{
    private ICurrentUser _caller = null!;

    public override Task<Result<CallerDto, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<CallerDto, IError>>(new CallerDto(
            _caller.Id,
            _caller.DisplayName,
            [.. _caller.Authorization.Roles],
            [.. _caller.Authorization.Permissions]));
}
