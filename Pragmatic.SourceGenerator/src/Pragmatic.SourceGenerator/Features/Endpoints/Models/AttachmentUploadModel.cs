using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Describes a multipart/form-data file-upload DomainAction endpoint
///     (generated for the [HasAttachments] trait).
///     <para>
///     The generated minimal-API handler accepts an <c>IFormFile</c> from the form,
///     reads its stream/metadata, and constructs the upload action with
///     <c>FileContent</c> / <c>FileName</c> / <c>FileSize</c> / <c>ContentType</c> bound.
///     </para>
/// </summary>
internal sealed record AttachmentUploadModel
{
    /// <summary>Action property receiving the file stream (e.g. "FileContent").</summary>
    public required string FileContentProperty { get; init; }

    /// <summary>Action property receiving the original file name (e.g. "FileName").</summary>
    public required string FileNameProperty { get; init; }

    /// <summary>Action property receiving the file size in bytes (e.g. "FileSize").</summary>
    public required string FileSizeProperty { get; init; }

    /// <summary>Action property receiving the MIME content type (e.g. "ContentType").</summary>
    public required string ContentTypeProperty { get; init; }

    /// <summary>Optional action property receiving a free-text description (e.g. "Description"). Null if unused.</summary>
    public string? DescriptionProperty { get; init; }

    /// <summary>Maximum file size in bytes (0 = unconstrained).</summary>
    public long MaxFileSizeBytes { get; init; }

    /// <summary>Allowed file extensions (lowercase, dot-prefixed). Empty = unconstrained.</summary>
    public EquatableArray<string> AllowedExtensions { get; init; } = EquatableArray<string>.Empty;
}
