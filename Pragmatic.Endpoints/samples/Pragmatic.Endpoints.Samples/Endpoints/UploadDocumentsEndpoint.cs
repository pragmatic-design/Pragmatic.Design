using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Response model for a batch document upload.
/// </summary>
public record BatchUploadResponse(int FileCount, long TotalBytes);

/// <summary>
///     Accepts multiple file uploads via <c>IFormFileCollection</c>.
///     Demonstrates:
///     <list type="bullet">
///         <item><c>[FromForm]</c> with <c>IFormFileCollection</c> for multi-file uploads</item>
///         <item><c>[MaxFileSize]</c> per-file validation (HTTP 413)</item>
///         <item><c>[AllowedContentTypes]</c> per-file validation (HTTP 415)</item>
///     </list>
/// </summary>
[Endpoint(HttpVerb.Post, "/documents/batch")]
[ApiSummary("Upload Documents (batch)")]
[ApiDescription("Accepts multiple documents as multipart/form-data. Each file is validated for size and type.")]
[ApiTags("Documents")]
[HttpStatus(201)]
public partial class UploadDocumentsEndpoint : Endpoint<BatchUploadResponse>
{
    /// <summary>
    ///     The documents to upload (multiple files).
    ///     <c>[MaxFileSize]</c> and <c>[AllowedContentTypes]</c> are enforced
    ///     at the HTTP boundary before <c>HandleAsync</c> is called.
    /// </summary>
    [FromForm]
    [MaxFileSize(25 * 1024 * 1024)] // 25 MB per file
    [AllowedContentTypes("application/pdf", "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public IFormFileCollection Documents { get; set; } = null!;

    /// <summary>
    ///     Optional category tag for all uploaded documents.
    /// </summary>
    [FromForm]
    public string? Category { get; set; }

    /// <inheritdoc />
    public override Task<Result<BatchUploadResponse>> HandleAsync(CancellationToken ct = default)
    {
        // At this point, all files have already passed size and content-type checks.
        var totalBytes = Documents.Sum(f => f.Length);

        return Task.FromResult<Result<BatchUploadResponse>>(
            new BatchUploadResponse(Documents.Count, totalBytes));
    }
}
