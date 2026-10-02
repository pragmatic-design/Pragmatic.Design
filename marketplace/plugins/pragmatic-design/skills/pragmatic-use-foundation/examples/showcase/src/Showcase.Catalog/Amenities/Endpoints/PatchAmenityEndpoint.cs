using Showcase.Catalog.Amenities.Mutations;

namespace Showcase.Catalog.Amenities.Endpoints;

/// <summary>
///     PATCH endpoint for partial amenity updates.
///     Demonstrates [GeneratePatch] with Optional{T} property-set tracking.
/// </summary>
/// <remarks>
///     The body is <c>{ "patch": { … } }</c>: <see cref="Patch" /> is a member of it, and a member present in
///     the patch is a member set. An amenity that does not exist is a 404, declared on the endpoint so the
///     contract says so.
/// </remarks>
[Endpoint(HttpVerb.Patch, "/{id:guid}")]
[EndpointGroup<AmenitiesGroup>]
[ApiSummary("Patch Amenity")]
[RequirePermission(CatalogPermissions.Amenity.Update)]
public partial class PatchAmenityEndpoint : Endpoint<PatchAmenityResult, NotFoundError>
{
    private IRepository<Amenity> _repository = null!;
    private IUnitOfWork _unitOfWork = null!;

    [FromRoute]
    public Guid Id { get; set; }

    [FromBody]
    public required PatchAmenityDto Patch { get; init; }

    public override async Task<Result<PatchAmenityResult, NotFoundError>> HandleAsync(CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (entity is null)
            return NotFoundError.For<Guid>("Amenity", Id);

        Patch.ApplyTo(entity);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new PatchAmenityResult(entity.Id, [.. Patch.ModifiedProperties]);
    }
}
