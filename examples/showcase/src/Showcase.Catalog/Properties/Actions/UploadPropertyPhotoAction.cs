using Microsoft.AspNetCore.Http;
using Pragmatic.Storage;

namespace Showcase.Catalog.Properties.Actions;

/// <summary>
/// Uploads a photo for a hotel property.
/// Demonstrates:
///   - [FromForm] binding for IFormFile in a DomainAction endpoint
///   - IFileStorage injection (provider-agnostic: disk in dev, Azure/S3 in prod)
///   - The generated handler emits DisableAntiforgery + Accepts multipart/form-data
/// </summary>
[DomainAction]
[Endpoint(HttpVerb.Post, "/api/properties/{propertyId}/photos")]
[ApiSummary("Upload Property Photo")]
[ApiTags("Properties")]
[RequirePermission(CatalogPermissions.Property.Update)]
[LoadEntity<Property>(nameof(PropertyId))]
public partial class UploadPropertyPhotoAction : DomainAction<Uri>
{
    // Injected by Actions SG (same pattern as _repository, _currentUser)
    private IFileStorage _storage = null!;

    public required Guid PropertyId { get; init; }

    /// <summary>The photo file — bound from multipart/form-data.</summary>
    [FromForm]
    public IFormFile Photo { get; set; } = null!;

    /// <summary>Optional caption for the photo.</summary>
    [FromForm]
    public string? Caption { get; set; }

    public override async Task<Result<Uri, IError>> Execute(CancellationToken ct = default)
    {
        // The property exists: [LoadEntity<Property>] answered 404 before this ran, after the permission
        // check.

        // Container names must be FLAT — Azure Blob (and other providers) forbid '/' in a container
        // name. Keep the container flat and carry the PropertyId in the file name so the stored object
        // still traces back to its property, without nesting the container path.
        var photoUri = await _storage
            .SaveAsync(Photo.OpenReadStream(), $"{PropertyId}-{Photo.FileName}", "property-photos", ct)
            .ConfigureAwait(false);

        return photoUri;
    }
}
