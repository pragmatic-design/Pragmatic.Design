# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Storage. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Reading the Stream Before Passing It to SaveAsync

**Wrong:**

```csharp
public override async Task<Result<Uri, IError>> Execute(CancellationToken ct)
{
    await using var stream = File.OpenReadStream();

    // Read the entire stream to validate content
    using var reader = new StreamReader(stream);
    var content = await reader.ReadToEndAsync(ct);
    if (content.Contains("malicious"))
        return ValidationError.For("File", "upload.prohibited_content");

    // SaveAsync receives an exhausted stream -- position is at the end
    var uri = await _storage.SaveAsync(stream, File.FileName, "imports", ct);
    return uri;
}
```

**Runtime result:** `SaveAsync` writes zero bytes because the stream position is at the end. The file is "saved" but empty. No exception is thrown.

**Right:**

```csharp
public override async Task<Result<Uri, IError>> Execute(CancellationToken ct)
{
    await using var stream = File.OpenReadStream();

    // Copy to a seekable MemoryStream for inspection
    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms, ct);

    ms.Position = 0;
    using var reader = new StreamReader(ms, leaveOpen: true);
    var content = await reader.ReadToEndAsync(ct);
    if (content.Contains("malicious"))
        return ValidationError.For("File", "upload.prohibited_content");

    ms.Position = 0;  // Reset before saving
    var uri = await _storage.SaveAsync(ms, File.FileName, "imports", ct);
    return uri;
}
```

**Why:** `IFormFile.OpenReadStream()` returns a forward-only stream. Once read, the position is at the end and subsequent reads return nothing. If you need to inspect the content before saving, copy to a `MemoryStream`, inspect, reset the position, then pass to `SaveAsync`.

---

## 2. Not Disposing the File Stream

**Wrong:**

```csharp
public override async Task<Result<Uri, IError>> Execute(CancellationToken ct)
{
    var stream = File.OpenReadStream();  // No await using!
    var uri = await _storage.SaveAsync(stream, File.FileName, "photos", ct);
    return uri;
    // Stream is never disposed -- resource leak
}
```

**Runtime result:** Works but leaks the stream handle. Under load, this can exhaust file handles or memory, especially with large uploads.

**Right:**

```csharp
public override async Task<Result<Uri, IError>> Execute(CancellationToken ct)
{
    await using var stream = File.OpenReadStream();
    var uri = await _storage.SaveAsync(stream, File.FileName, "photos", ct);
    return uri;
}
```

**Why:** `IFormFile.OpenReadStream()` returns a stream that holds resources (file handles, buffers). The caller owns the stream lifetime and must dispose it. Use `await using` to ensure cleanup even if `SaveAsync` throws.

---

## 3. Hardcoding the Base Path for LocalDiskFileStorage

**Wrong:**

```csharp
app.UseStorage(sp => new LocalDiskFileStorage(
    @"C:\inetpub\wwwroot\myapp",  // Hardcoded path!
    sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
```

**Runtime result:** Works on the developer's machine. Fails on CI, Docker, Linux deployments, or any machine without that exact path.

**Right:**

```csharp
app.UseStorage(sp => new LocalDiskFileStorage(
    Path.Combine(app.Environment.ContentRootPath, "wwwroot"),  // Resolved from the host environment
    sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
    maxFileSizeBytes: 10 * 1024 * 1024));
```

Or with `AddLocalDiskStorage`:

```csharp
builder.Services.AddLocalDiskStorage(builder.Environment.WebRootPath);
```

**Why:** Deriving the base path from the host environment (`IWebHostEnvironment.WebRootPath`, or `ContentRootPath + "wwwroot"` on `IPragmaticBuilder.Environment`) resolves to the correct directory in every deployment. A hardcoded path does not fail loudly on other machines: `LocalDiskFileStorage` creates the full directory tree on first save, so files land in a location the static-files middleware never serves — saves succeed, downloads 404.

---

## 4. Using the Same IFileStorage Registration Twice

**Wrong:**

```csharp
// Program.cs
builder.Services.AddLocalDiskStorage(builder.Environment.WebRootPath);

// Later in the same file or in a startup step
app.UseStorage<AzureBlobFileStorage>();
```

**Runtime result:** Both register `IFileStorage` as a singleton. The last registration wins -- `AzureBlobFileStorage` is used. `AddLocalDiskStorage` has no effect. No warning is emitted.

**Right:**

Choose one registration based on the environment:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseStorage(sp => new LocalDiskFileStorage(
        webRootPath,
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
        maxFileSizeBytes: 10 * 1024 * 1024));
}
else
{
    app.UseStorage<AzureBlobFileStorage>();  // requires its ctor dependencies registered — see mistake 8
}
```

**Why:** `IFileStorage` is a singleton with a single implementation. DI "last registration wins" means the second call silently replaces the first. This is by design (it enables overriding), but accidental double registration leads to confusion about which provider is active.

---

## 5. Storing the File URI as a String Instead of a Uri

**Wrong:**

```csharp
public class Document
{
    public string FileUrl { get; set; } = "";  // String, not Uri
}

// In the action
var uri = await _storage.SaveAsync(stream, File.FileName, "documents", ct);
document.FileUrl = uri.ToString();

// Later, when deleting
await _storage.DeleteAsync(new Uri(document.FileUrl));  // May fail for relative URIs!
```

**Runtime result:** `new Uri("/files/documents/abc.pdf")` throws `UriFormatException` because the `Uri` constructor treats a relative path as invalid without specifying `UriKind.Relative`. The entity was saved with a relative string, but `DeleteAsync` cannot reconstruct the `Uri`.

**Right:**

```csharp
public class Document
{
    public Uri FileUri { get; set; } = null!;  // Store as Uri
}

// In the action
document.FileUri = await _storage.SaveAsync(stream, File.FileName, "documents", ct);

// Later, when deleting
await _storage.DeleteAsync(document.FileUri);  // Same Uri object, always works
```

**Why:** `LocalDiskFileStorage` returns relative URIs (`new Uri("/files/...", UriKind.Relative)`). Storing as `string` and reconstructing with `new Uri(string)` fails because the default constructor requires absolute URIs. Store the `Uri` directly and pass it back to `DeleteAsync` without conversion.

---

## 6. Not Calling UseStaticFiles for LocalDiskFileStorage

**Wrong:**

```csharp
builder.Services.AddLocalDiskStorage(builder.Environment.WebRootPath);

var app = builder.Build();
// Missing: app.UseStaticFiles();
app.MapPragmaticEndpoints();
```

**Runtime result:** Files are saved successfully to `wwwroot/files/photos/abc.jpg`, but requesting `/files/photos/abc.jpg` returns 404. The file exists on disk but ASP.NET Core does not serve it.

**Right:**

```csharp
builder.Services.AddLocalDiskStorage(builder.Environment.WebRootPath);

var app = builder.Build();
app.UseStaticFiles();  // Required to serve files from wwwroot/
app.MapPragmaticEndpoints();
```

**Why:** `LocalDiskFileStorage` writes files under `wwwroot/files/` and returns relative URIs that map to static file paths. But ASP.NET Core does not serve static files unless `UseStaticFiles()` is in the middleware pipeline. Without it, the files are saved but inaccessible via HTTP.

---

## 7. Using LocalDiskFileStorage in Production

**Wrong:**

```csharp
// appsettings.Production.json has no storage config override
// Program.cs always registers local disk
builder.Services.AddLocalDiskStorage(builder.Environment.WebRootPath);
```

**Production issues:**
- Files are lost on container restart (ephemeral storage)
- No CDN, no geographic distribution
- No backup or redundancy
- Disk space limits on cloud VMs
- Horizontal scaling fails (each instance has its own local disk)

**Right:**

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseStorage(sp => new LocalDiskFileStorage(
        webRootPath,
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
        maxFileSizeBytes: 10 * 1024 * 1024));
}
else
{
    app.UseStorage(sp => new AzureBlobFileStorage(   // Or S3FileStorage
        new BlobServiceClient(app.Configuration.GetConnectionString("BlobStorage")),
        new AzureBlobStorageOptions(),
        sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));
}
```

**Why:** `LocalDiskFileStorage` is designed for development and demos. It has no persistence guarantees, no replication, and no CDN integration. In production, use a cloud storage provider that offers durability, availability, and scalability. The `IFileStorage` abstraction makes switching trivial -- zero changes to domain code.

---

## 8. Forgetting to Register the Cloud Provider's Dependencies

**Wrong:**

```csharp
app.UseStorage<AzureBlobFileStorage>();
// But BlobServiceClient is not registered!
```

**Runtime result:** `InvalidOperationException` at first file upload: "Unable to resolve service for type 'Azure.Storage.Blobs.BlobServiceClient'." The storage service is registered but its dependency is not.

**Right:**

Either register every constructor dependency, then use the type-based overload:

```csharp
// AzureBlobFileStorage(BlobServiceClient, AzureBlobStorageOptions, ILogger<...>)
builder.Services.AddSingleton(new BlobServiceClient(connectionString));
builder.Services.AddSingleton(new AzureBlobStorageOptions { ContainerPrefix = "myapp-" });

app.UseStorage<AzureBlobFileStorage>();
```

Or build everything in one place with the factory overload — no separate registrations needed:

```csharp
app.UseStorage(sp => new AzureBlobFileStorage(
    new BlobServiceClient(connectionString),
    new AzureBlobStorageOptions { ContainerPrefix = "myapp-" },
    sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));
```

**Why:** `UseStorage<T>()` registers `T` as `IFileStorage` via `AddSingleton<IFileStorage, T>()`. The DI container constructs `T` by resolving its constructor parameters — for `AzureBlobFileStorage` that means `BlobServiceClient` *and* `AzureBlobStorageOptions` (for `S3FileStorage`: `IAmazonS3` and `S3StorageOptions`). The storage registration does not register the provider's dependencies. The factory overload sidesteps the problem by constructing the provider explicitly.

---

## 9. Using Container Names with Special Characters

**Wrong:**

```csharp
var uri = await _storage.SaveAsync(stream, file.FileName, "user uploads/2024", ct);
```

**Runtime result:** Each provider interprets the container string differently, so the same code behaves differently per backend:

- **LocalDisk** treats `/` as a path separator and creates *nested* directories (`files/user uploads/2024/`). It works, and nested containers are explicitly allowed — but only here.
- **Azure Blob** maps the container to a blob container name, and Azure container names cannot contain spaces or slashes (lowercase alphanumeric + hyphens, 3-63 chars). The operation throws.
- **S3** embeds the container into the object key (`{KeyPrefix}{container}/{guid}{ext}`), so a slash silently becomes an extra key segment. It works, but the layout no longer matches the other providers.

**Right:**

```csharp
var uri = await _storage.SaveAsync(stream, file.FileName, "user-uploads", ct);
```

**Why:** This is a portability rule. The container name is the one part of the storage contract that every provider maps onto a different native concept (directory, blob container, key prefix). The intersection that works identically everywhere is: lowercase, alphanumeric + hyphens, no spaces, no slashes. Code written against that intersection switches providers with zero changes; code that relies on nested containers works on LocalDisk and S3 but throws on Azure.

---

## 10. Deleting Files Without Cleaning Up Entity References

**Wrong:**

```csharp
public override async Task<VoidResult> Execute(CancellationToken ct)
{
    // Delete the file
    await _storage.DeleteAsync(document.FileUri, ct);

    // Forgot to remove or update the entity!
    // document.FileUri now points to a deleted file
}
```

**Runtime result:** The file is deleted from storage, but the entity still holds the old URI. Any subsequent request to serve that URI returns 404 (local disk) or an error (cloud). The entity appears to have a valid attachment but the file is gone.

**Right:**

```csharp
public override async Task<VoidResult> Execute(CancellationToken ct)
{
    // Delete the file
    await _storage.DeleteAsync(document.FileUri, ct);

    // Remove the entity (or clear the URI)
    await _repository.RemoveAsync(document, ct);
    await _repository.SaveAsync(ct);

    return VoidResult.Success();
}
```

**Why:** `IFileStorage` and your persistence layer are separate systems with no transactional guarantee. Always update or delete the entity reference when deleting the physical file, and vice versa. For maximum safety, delete the entity first (preventing further access) and then delete the physical file.

---

## 11. Trusting the Content Type Derived from the File Name

**Wrong:**

```csharp
// Accept any upload; the stored content-type comes from the extension
await using var stream = File.OpenReadStream();
var uri = await _storage.SaveAsync(stream, File.FileName, "photos", ct);

// Later, serve the file inline in the browser trusting that content-type
```

**Runtime result:** The Azure and S3 providers set the object's content type via `MimeTypes.GetMimeType(extension)` — a pure extension lookup, no content sniffing. The file name is attacker-controlled, so a file named `avatar.jpg` containing HTML/JavaScript is stored and served as `image/jpeg`... unless the browser sniffs it. Served inline from your domain, that is a stored-XSS vector.

**Right:**

```csharp
await using var stream = File.OpenReadStream();

// Validate the actual content before saving — magic bytes, an allow-list, or a real decoder
using var ms = new MemoryStream();
await stream.CopyToAsync(ms, ct);
ms.Position = 0;
if (!ImageValidator.IsValidImage(ms))            // your application-level check
    return ValidationError.For("File", "upload.not_an_image");

ms.Position = 0;
var uri = await _storage.SaveAsync(ms, File.FileName, "photos", ct);
```

When serving user-uploaded files, also send `X-Content-Type-Options: nosniff` and prefer `Content-Disposition: attachment` for anything you have not validated.

**Why:** `MimeTypes.GetMimeType` maps an extension to a MIME type — that is all. Content validation is an application responsibility: the storage layer cannot know whether the bytes match the claimed type. Validate on upload (magic bytes / allow-list / decode) if files are ever served inline from your origin.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| Reading stream before SaveAsync | Empty file saved, zero bytes |
| Not disposing stream | Resource leak under load |
| Hardcoded base path | Fails on any machine without that exact path |
| Double IFileStorage registration | Wrong provider silently active |
| Storing URI as string | `UriFormatException` on relative URIs when reconstructing |
| Missing UseStaticFiles | Files saved but 404 on HTTP requests |
| LocalDisk in production | Files lost on restart, no CDN, no scaling |
| Missing cloud provider dependencies | `InvalidOperationException` at first upload |
| Special characters in container names | Works on LocalDisk/S3, throws on Azure — not portable |
| Deleting files without entity cleanup | Dangling URI references, 404 for users |
| Trusting extension-derived content type | Stored XSS when unvalidated uploads are served inline |
