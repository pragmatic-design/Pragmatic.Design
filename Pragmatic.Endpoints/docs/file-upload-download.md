# File Upload and Download

## The Problem

File handling in APIs is inherently different from JSON request/response patterns. Uploads require multipart form data, content type validation, and size limits. Downloads need correct MIME types, content disposition headers, conditional requests (ETag/If-None-Match), and range request support for large files. Getting these details wrong leads to security vulnerabilities (unrestricted uploads), poor UX (no resume on large downloads), or silent data corruption.

Pragmatic.Endpoints provides first-class support for both directions: upload via `[FromForm]` with `IFormFile` and validation attributes, and download via the `FileResponse` record with automatic ETag and range request handling.

---

## File Upload

### Basic Upload

Use ASP.NET Core's `[FromForm]` attribute on `IFormFile` properties:

```csharp
[Endpoint(HttpVerb.Post, "/api/documents")]
public partial class UploadDocument : Endpoint<DocumentDto>
{
    [FromForm]
    public required IFormFile File { get; set; }

    public override async Task<Result<DocumentDto, ValidationError>> HandleAsync(CancellationToken ct)
    {
        // File.FileName, File.ContentType, File.Length, File.OpenReadStream()
        var stream = File.OpenReadStream();
        // ... process the file
    }
}
```

The SG detects `[FromForm]` properties and generates the minimal API lambda with `[FromForm]` parameter binding. It also emits `builder.DisableAntiforgery()` (required for minimal API file uploads) and `builder.Accepts<IFormFile>("multipart/form-data")` for OpenAPI metadata.

### Multiple Files

For multiple file uploads, use `IFormFileCollection`:

```csharp
[Endpoint(HttpVerb.Post, "/api/photos/batch")]
public partial class UploadPhotos : Endpoint<BatchUploadResult>
{
    [FromForm]
    public required IFormFileCollection Photos { get; set; }
}
```

### Mixed Form Data

You can combine files with other form fields:

```csharp
[Endpoint(HttpVerb.Post, "/api/documents")]
public partial class UploadDocument : Endpoint<DocumentDto>
{
    [FromForm]
    public required IFormFile File { get; set; }

    [FromForm]
    public required string Title { get; set; }

    [FromForm]
    public string? Description { get; set; }
}
```

Form parameters and body parameters are mutually exclusive. When `[FromForm]` properties are present, the SG does not generate a `[FromBody]` DTO.

---

## Upload Validation

### `[MaxFileSize]`

Limits the file size in bytes. The SG generates a check before `HandleAsync` that returns HTTP 413 (Payload Too Large) if the file exceeds the limit.

```csharp
[FromForm]
[MaxFileSize(10 * 1024 * 1024)]  // 10 MB
public required IFormFile Photo { get; set; }
```

The attribute takes a `long maxBytes` constructor parameter:

```csharp
[MaxFileSize(5 * 1024 * 1024)]         // 5 MB
[MaxFileSize(500 * 1024)]               // 500 KB
[MaxFileSize(1L * 1024 * 1024 * 1024)]  // 1 GB
```

The generated validation:

```csharp
if (photo.Length > 10485760L)
    return Microsoft.AspNetCore.Http.Results.Problem(
        "File 'Photo' exceeds the maximum allowed size of 10 MB.",
        statusCode: 413);
```

The error message includes a human-readable file size (bytes, KB, MB, or GB).

### `[AllowedContentTypes]`

Restricts which MIME content types are accepted. The SG generates a check returning HTTP 415 (Unsupported Media Type) for files with disallowed content types.

```csharp
[FromForm]
[AllowedContentTypes("image/jpeg", "image/png", "image/webp")]
public required IFormFile Photo { get; set; }
```

The attribute accepts `params string[]`:

```csharp
[AllowedContentTypes("application/pdf")]
[AllowedContentTypes("image/jpeg", "image/png", "image/gif", "image/webp")]
[AllowedContentTypes("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
```

The generated validation:

```csharp
if (!new[] { "image/jpeg", "image/png", "image/webp" }.Contains(photo.ContentType, System.StringComparer.OrdinalIgnoreCase))
    return Microsoft.AspNetCore.Http.Results.Problem(
        $"File 'Photo' has unsupported content type '{photo.ContentType}'. Allowed: image/jpeg, image/png, image/webp",
        statusCode: 415);
```

### Combining Both

```csharp
[FromForm]
[MaxFileSize(5 * 1024 * 1024)]
[AllowedContentTypes("image/jpeg", "image/png")]
public required IFormFile Avatar { get; set; }
```

Size check runs first, then content type check. Both happen before `HandleAsync`.

### Collection Validation

For `IFormFileCollection`, each file in the collection is validated individually:

```csharp
[FromForm]
[MaxFileSize(2 * 1024 * 1024)]
[AllowedContentTypes("image/jpeg", "image/png")]
public required IFormFileCollection Photos { get; set; }
```

The SG generates a `foreach` loop that checks every file:

```csharp
foreach (var file in photos)
{
    if (file.Length > 2097152L)
        return Results.Problem(
            $"File '{file.FileName}' in 'Photos' exceeds the maximum allowed size of 2 MB.",
            statusCode: 413);

    if (!new[] { "image/jpeg", "image/png" }.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        return Results.Problem(
            $"File '{file.FileName}' in 'Photos' has unsupported content type '{file.ContentType}'. Allowed: image/jpeg, image/png",
            statusCode: 415);
}
```

---

## Global Defaults via PragmaticEndpointsOptions

For files without explicit `[MaxFileSize]` or `[AllowedContentTypes]`, you can set global defaults:

```csharp
services.AddPragmaticEndpoints(options =>
{
    options.MaxUploadFileSize = 25 * 1024 * 1024;  // 25 MB global max
    options.DefaultAllowedContentTypes = new[]
    {
        "image/*",          // Wildcard: any image type
        "application/pdf",
        "text/csv"
    };
});
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxUploadFileSize` | `long?` | `null` (no limit) | Global max file size in bytes |
| `DefaultAllowedContentTypes` | `string[]?` | `null` (all types) | Global allowed MIME types |

### Per-Property Override Logic

The SG applies validation in this priority:

1. **Explicit attributes on the property** -- `[MaxFileSize]` and `[AllowedContentTypes]` on the property take priority. Global defaults are not checked for these properties.
2. **Global defaults from PragmaticEndpointsOptions** -- applied only to `IFormFile` properties that have neither `[MaxFileSize]` nor `[AllowedContentTypes]`.

This means you can set conservative global defaults and override specific properties:

```csharp
// Global: 10 MB, images only
options.MaxUploadFileSize = 10 * 1024 * 1024;
options.DefaultAllowedContentTypes = new[] { "image/*" };

// This endpoint: explicit override for larger PDFs
[FromForm]
[MaxFileSize(50 * 1024 * 1024)]  // 50 MB override
[AllowedContentTypes("application/pdf")]
public required IFormFile Document { get; set; }

// This property: no attributes -> uses global defaults (10 MB, images only)
[FromForm]
public required IFormFile Thumbnail { get; set; }
```

### Wildcard Content Type Matching

Global defaults support wildcard matching with `*`:

```csharp
options.DefaultAllowedContentTypes = new[] { "image/*", "application/pdf" };
```

The `image/*` pattern matches `image/jpeg`, `image/png`, `image/webp`, etc. The matching is prefix-based: the `*` is removed and the remaining string is compared against the start of the file's content type.

---

## File Download: FileResponse

### The FileResponse Record

`FileResponse` is a sealed record that represents a file download response:

```csharp
public sealed record FileResponse(
    Stream Content,         // The file content stream
    string ContentType,     // MIME type (e.g., "application/pdf")
    string? FileName = null,       // File name for Content-Disposition
    bool EnableRangeProcessing = false)  // Enable Accept-Ranges: bytes
{
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public bool Inline { get; init; }  // true = display in browser, false = download
}
```

| Property | Type | Description |
|----------|------|-------------|
| `Content` | `Stream` | File content (required) |
| `ContentType` | `string` | MIME type (required) |
| `FileName` | `string?` | Download file name. When `Inline` is true, this is not sent |
| `EnableRangeProcessing` | `bool` | Whether to allow partial content requests |
| `ETag` | `string?` | Entity tag for conditional requests |
| `LastModified` | `DateTimeOffset?` | Last modification date for conditional requests |
| `Inline` | `bool` | `true` = `Content-Disposition: inline`, `false` = `attachment` |
| `PartialContent` | `FileContentRange?` | Set when `Content` is already one byte range — see [Downloads Across a Remote Boundary](#downloads-across-a-remote-boundary) |

### Basic Download

```csharp
[Endpoint(HttpVerb.Get, "/api/documents/{id}/download")]
public partial class DownloadDocument : Endpoint<FileResponse, NotFoundError>
{
    public required Guid Id { get; init; }

    public override async Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var doc = await _storage.GetAsync(Id, ct);
        if (doc is null)
            return NotFoundError.For("Document", Id);

        return new FileResponse(doc.Stream, doc.ContentType, doc.FileName);
    }
}
```

### ETag and Conditional Requests

When you set the `ETag` property, the framework automatically handles `If-None-Match` conditional requests:

```csharp
return new FileResponse(stream, "application/pdf", "report.pdf")
{
    ETag = doc.ContentHash,  // e.g., MD5 or SHA256 of the content
    LastModified = doc.ModifiedAt
};
```

The `FileResponseExtensions.ToResult()` method (in `Pragmatic.Endpoints.AspNetCore`) checks:

1. **If-None-Match**: If the request includes an `If-None-Match` header matching the response's ETag, returns HTTP 304 (Not Modified) with no body.
2. **If-Modified-Since**: If the request includes `If-Modified-Since` and the file hasn't been modified since that date, returns HTTP 304.

This means repeat requests from clients that cache properly will not transfer the file content again, saving bandwidth.

### Range Requests

For large files (videos, archives), enable range processing to allow partial downloads and resume:

```csharp
return new FileResponse(stream, "video/mp4", "presentation.mp4")
{
    EnableRangeProcessing = true
};
```

When enabled, ASP.NET Core handles `Range` request headers and returns HTTP 206 (Partial Content) with the requested byte range. This is critical for:

- Video streaming (seeking to a specific position)
- Download resume after interruption
- Parallel download of file chunks

### Inline Display

For files that should be displayed in the browser (images, PDFs) rather than downloaded:

```csharp
return new FileResponse(stream, "image/jpeg")
{
    Inline = true  // Content-Disposition: inline
};
```

When `Inline` is `true`, the `FileName` is not included in the `Content-Disposition` header, which tells the browser to display the content directly.

### Downloads Across a Remote Boundary

A file action reached through `[RemoteBoundary<TModule>]` streams the same `FileResponse` back to the caller. Two parts of the record cannot ride on standard headers, so they travel on their own, defined once in `PragmaticFileHeaders`:

| Header | Carries |
|--------|---------|
| `X-Pragmatic-File-Name` | `FileName`, percent-encoded UTF-8 |
| `X-Pragmatic-File-Inline` | `Inline` |

`Content-Disposition` is still emitted exactly as it always was — these headers are an extra channel for boundary-to-boundary RPC, not a replacement. `ETag` and `LastModified` need no extra header: they already travel as `ETag` and `Last-Modified` (the latter to whole-second precision).

`Range` and `If-Range` are propagated end to end. The host forwards them onto the `POST /_pragmatic/invoke` call, the boundary reads only the requested bytes out of storage and answers `206`, and the host passes that response through untouched — status, `Content-Range` and `Accept-Ranges` as received. A seek into a 500 MB object therefore moves the requested bytes, not 500 MB.

The pass-through matters: an already-partial body handed back to `Results.File(..., enableRangeProcessing: true)` would have the caller's `Range` applied to it a second time, producing a range of a range under a plausible status and a plausible `Content-Range`. `FileResponse.PartialContent` marks such a body as already sliced, and `ToResult` forwards rather than re-interprets it. An unsatisfiable range is answered `416` with `Content-Range: bytes */length`.

---

## IETagSupport Interface

For non-file responses that should support ETag-based caching, implement `IETagSupport`:

```csharp
public record ProductResponse : IETagSupport
{
    public Guid Id { get; init; }
    public string Name { get; init; }
    public string? ETag { get; set; }
}
```

This is a contract interface for response DTOs. The actual ETag handling (setting the ETag header, checking If-None-Match) depends on your middleware or endpoint implementation. `FileResponse` handles this automatically; for non-file responses, you would need to set the response headers manually or use a post-processor.

---

## Complete Upload Example

```csharp
// Global defaults
services.AddPragmaticEndpoints(options =>
{
    options.MaxUploadFileSize = 20 * 1024 * 1024;     // 20 MB
    options.DefaultAllowedContentTypes = new[]
    {
        "image/*",
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.*"
    };
});
```

```csharp
// Profile photo: small image, strict types
[Endpoint(HttpVerb.Post, "/api/users/{id}/avatar")]
public partial class UploadAvatar : Endpoint<AvatarDto>
{
    public required Guid Id { get; init; }

    [FromForm]
    [MaxFileSize(2 * 1024 * 1024)]  // 2 MB (overrides global 20 MB)
    [AllowedContentTypes("image/jpeg", "image/png", "image/webp")]
    public required IFormFile Avatar { get; set; }

    public override async Task<Result<AvatarDto, ValidationError>> HandleAsync(CancellationToken ct)
    {
        // Avatar.Length, Avatar.ContentType already validated by SG
        await _storage.SaveAsync($"avatars/{Id}", Avatar.OpenReadStream(), ct);
        return new AvatarDto(Id, $"/api/users/{Id}/avatar");
    }
}

// Document upload: uses global defaults (20 MB, images + PDF + Office)
[Endpoint(HttpVerb.Post, "/api/documents")]
public partial class UploadDocument : Endpoint<DocumentDto>
{
    [FromForm]
    public required IFormFile File { get; set; }

    [FromForm]
    public required string Title { get; set; }
}

// Batch photo upload with per-file validation
[Endpoint(HttpVerb.Post, "/api/gallery/upload")]
public partial class UploadGalleryPhotos : Endpoint<GalleryUploadResult>
{
    [FromForm]
    [MaxFileSize(10 * 1024 * 1024)]
    [AllowedContentTypes("image/jpeg", "image/png")]
    public required IFormFileCollection Photos { get; set; }
}
```

## Complete Download Example

```csharp
[Endpoint(HttpVerb.Get, "/api/documents/{id}/download")]
public partial class DownloadDocument : Endpoint<FileResponse, NotFoundError>
{
    public required Guid Id { get; init; }

    private readonly IDocumentStorage _storage;

    public override async Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var doc = await _storage.GetAsync(Id, ct);
        if (doc is null)
            return NotFoundError.For("Document", Id);

        return new FileResponse(doc.Stream, doc.ContentType, doc.FileName)
        {
            ETag = doc.ContentHash,
            LastModified = doc.ModifiedAt,
            EnableRangeProcessing = doc.Size > 1024 * 1024  // Range requests for files > 1 MB
        };
    }
}

// Inline display (e.g., image preview)
[Endpoint(HttpVerb.Get, "/api/photos/{id}")]
public partial class GetPhoto : Endpoint<FileResponse, NotFoundError>
{
    public required Guid Id { get; init; }

    public override async Task<Result<FileResponse, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var photo = await _storage.GetAsync(Id, ct);
        if (photo is null)
            return NotFoundError.For("Photo", Id);

        return new FileResponse(photo.Stream, photo.ContentType)
        {
            Inline = true,  // Display in browser
            ETag = photo.Hash
        };
    }
}
```

---

## Notes

- **HTTP-boundary validation**: `[MaxFileSize]` and `[AllowedContentTypes]` run before `HandleAsync`. They are not business-logic validation -- they are HTTP-layer guards that reject bad requests before your code executes.
- **Kestrel limits**: The `[MaxFileSize]` attribute validates individual file sizes, but Kestrel has its own request body size limit (default 28.6 MB). For large file uploads, you may need to configure `KestrelServerOptions.Limits.MaxRequestBodySize` as well.
- **Antiforgery disabled**: The SG automatically calls `builder.DisableAntiforgery()` for endpoints with form parameters. This is required for minimal API file uploads.
- **Content type is client-reported**: `[AllowedContentTypes]` checks the `Content-Type` header sent by the client. Malicious clients can lie about content types. For security-critical validation, also inspect the file content (magic bytes) in your business logic.
- **FileResponse → Results.File()**: The SG (and `FileResponseExtensions`) converts `FileResponse` to ASP.NET Core's `Results.File()`, which handles all the HTTP details (Content-Disposition, Accept-Ranges, ETag headers, etc.).
