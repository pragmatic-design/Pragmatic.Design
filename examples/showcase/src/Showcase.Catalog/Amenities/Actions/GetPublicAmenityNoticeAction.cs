namespace Showcase.Catalog.Amenities.Actions;

/// <summary>
/// A public notice about the amenities, open to an anonymous caller only while the anonymous role holds the permission.
///
/// Demonstrates: <c>[AllowAnonymous]</c> with <c>[RequirePermission]</c>. The route needs no sign-in, and the operation still asks for a
/// permission: an anonymous caller holds the host's anonymous role (<c>authz.AnonymousRole</c>), so an administrator opens it by granting
/// the permission to that role and closes it by revoking it, without a deploy. Lacking the permission the answer is 401, as for any
/// anonymous caller.
/// </summary>
[DomainAction]
[Endpoint(HttpVerb.Get, "api/amenities/public-notice")]
[ApiSummary("Get Public Amenity Notice")]
[ApiTags("Amenities")]
[AllowAnonymous]
[RequirePermission(CatalogPermissions.Amenity.Read)]
public partial class GetPublicAmenityNoticeAction : DomainAction<string>
{
    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<string, IError>>("Amenities are open to the public.");
}
