using Pragmatic.Endpoints.Attributes;
using Pragmatic.Endpoints.Base;
using Pragmatic.Endpoints.Responses;
using Pragmatic.Endpoints.Samples.Errors;
using Pragmatic.Result;

namespace Pragmatic.Endpoints.Samples.Endpoints;

/// <summary>
///     Returns a file download via <see cref="FileResponse" />.
///     Demonstrates:
///     <list type="bullet">
///         <item><c>FileResponse</c> return type — auto-mapped to <c>Results.File()</c></item>
///         <item>Conditional requests via <c>ETag</c> / <c>LastModified</c> (304 Not Modified)</item>
///         <item>Range request support for large files (<c>EnableRangeProcessing</c>)</item>
///         <item>Inline vs attachment disposition</item>
///     </list>
/// </summary>
[Endpoint(HttpVerb.Get, "/documents/{id}/download")]
[ApiSummary("Download Document")]
[ApiDescription("Downloads a document by ID with ETag and range request support.")]
[ApiTags("Documents")]
public partial class DownloadDocumentEndpoint : Endpoint<FileResponse, NotFoundError>
{
    /// <summary>
    ///     The document identifier.
    /// </summary>
    [FromRoute]
    public Guid Id { get; set; }

    /// <summary>
    ///     Whether to display inline (in browser) rather than as an attachment.
    /// </summary>
    [FromQuery]
    public bool? Inline { get; set; }

    /// <inheritdoc />
    public override Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct = default)
    {
        // Simulate document lookup — in production this would query a storage service
        if (Id == Guid.Empty)
            return Task.FromResult<Result<FileResponse, NotFoundError>>(
                new NotFoundError { ResourceType = "Document", ResourceId = Id.ToString() });

        // Create a sample PDF content stream
        var content = new MemoryStream("Sample document content"u8.ToArray());

        var response = new FileResponse(content, "application/pdf", $"document-{Id}.pdf")
        {
            ETag = $"\"{Id:N}\"",
            LastModified = DateTimeOffset.UtcNow.AddHours(-1),
            EnableRangeProcessing = true,
            Inline = Inline ?? false
        };

        return Task.FromResult<Result<FileResponse, NotFoundError>>(response);
    }
}
