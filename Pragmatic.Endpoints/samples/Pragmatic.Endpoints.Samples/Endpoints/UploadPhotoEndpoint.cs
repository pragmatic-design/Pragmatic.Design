using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Response model for a successfully uploaded photo.
/// </summary>
public record PhotoResponse(Guid PhotoId, string FileName, long SizeBytes, string? Caption);

/// <summary>
///     Accepts a multipart form upload containing a photo file and an optional caption.
///     Demonstrates <c>[FromForm]</c> binding with <c>IFormFile</c> on a <c>DomainAction</c>.
/// </summary>
/// <remarks>
///     Generator behavior:
///     - Detects <c>IFormFile</c> property and emits <c>DisableAntiforgery()</c> automatically.
///     - Non-file <c>[FromForm]</c> properties become additional form fields.
///     - No body DTO generated; multipart is handled directly.
/// </remarks>
[DomainAction]
[Endpoint(HttpVerb.Post, "/photos")]
[ApiSummary("Upload Photo")]
[ApiDescription("Accepts a multipart/form-data upload with a photo file and optional caption.")]
[ApiTags("Media")]
[HttpStatus(201)]
public partial class UploadPhotoEndpoint : DomainAction<PhotoResponse, ValidationError>
{
    /// <summary>
    ///     The photo file to upload.
    ///     <c>[MaxFileSize]</c> and <c>[AllowedContentTypes]</c> are enforced at the HTTP boundary
    ///     (before <c>Execute</c> is called), returning 413 or 415 automatically.
    ///     Presence of <c>IFormFile</c> triggers <c>DisableAntiforgery()</c> in the generated handler.
    /// </summary>
    [FromForm]
    [MaxFileSize(10 * 1024 * 1024)] // 10 MB
    [AllowedContentTypes("image/jpeg", "image/png", "image/webp")]
    public IFormFile Photo { get; set; } = null!;

    /// <summary>
    ///     Optional descriptive caption for the photo.
    /// </summary>
    [FromForm]
    public string? Caption { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     File size and content-type checks are handled automatically by the generated handler
    ///     via <c>[MaxFileSize]</c> and <c>[AllowedContentTypes]</c> — no manual validation needed.
    /// </remarks>
    public override async Task<Result<PhotoResponse, IError>> Execute(CancellationToken ct = default)
    {
        // File is already validated (size ≤ 10 MB, type ∈ jpeg/png/webp).
        // In production, stream the file to blob storage here.
        await using var stream = Photo.OpenReadStream();
        _ = stream; // consume for sample purposes

        return new PhotoResponse(
            PhotoId: Guid.NewGuid(),
            FileName: Photo.FileName,
            SizeBytes: Photo.Length,
            Caption: Caption);
    }
}
