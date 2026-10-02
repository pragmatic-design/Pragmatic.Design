# Getting Started with Pragmatic.Storage

This guide walks through the full file life cycle in a Pragmatic.Design application: upload
(`SaveAsync`), read back (`GetAsync`/`ExistsAsync`), delete (`DeleteAsync`), and serving files to
clients.

## Prerequisites

- `Pragmatic.Storage` NuGet package referenced
- ASP.NET Core host with `UseStaticFiles()` configured (for local disk)

## Step 1: Register the Storage Provider

### Development (local disk)

In your `Program.cs`:

```csharp
using Pragmatic.Storage;
using Pragmatic.Storage.Local;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseStorage(sp => new LocalDiskFileStorage(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
        maxFileSizeBytes: 10 * 1024 * 1024));   // reject uploads over 10 MB
});
```

The third constructor parameter caps upload size: `0` (the default) means no limit; a positive
value rejects oversized uploads before they fill the disk — enforced up front for seekable streams
and mid-copy for non-seekable ones (HTTP bodies).

Or using the `IServiceCollection` extension directly:

```csharp
builder.Services.AddLocalDiskStorage(builder.Environment.WebRootPath);                       // no limit
builder.Services.AddLocalDiskStorage(builder.Environment.WebRootPath, 10 * 1024 * 1024);     // 10 MB cap
```

The single-argument overload registers the storage with no size limit; the two-argument overload
caps uploads at `maxFileSizeBytes`.

### Production (cloud provider)

Swap to a cloud provider without changing any domain code. The provider constructors take the SDK
client and an options object, so register them with the factory overload:

```csharp
using Azure.Storage.Blobs;
using Pragmatic.Storage.Azure;

app.UseStorage(sp => new AzureBlobFileStorage(
    new BlobServiceClient(app.Configuration.GetConnectionString("BlobStorage")),
    new AzureBlobStorageOptions { ContainerPrefix = "myapp-" },   // "myapp-" + "photos" → container "myapp-photos"
    sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));
```

For S3 / Cloudflare R2:

```csharp
using Amazon.S3;
using Pragmatic.Storage.S3;

app.UseStorage(sp => new S3FileStorage(
    new AmazonS3Client(),   // region + credentials from the environment
    new S3StorageOptions
    {
        BucketName = "my-app-uploads",
        KeyPrefix = "uploads/",
        PublicBaseUrl = "https://cdn.example.com/uploads",   // omit to get s3:// URIs
        MaxFileSizeBytes = 10 * 1024 * 1024,
    },
    sp.GetRequiredService<ILogger<S3FileStorage>>()));
```

Each provider package also ships a DI helper on `IServiceCollection` that registers the provider from
an already-registered SDK client:

```csharp
// Azure — register the client (or pass a connection string to the overload), then the provider
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration.GetConnectionString("BlobStorage")));
builder.Services.AddAzureBlobStorage(new AzureBlobStorageOptions { ContainerPrefix = "myapp-" });
// Or in one call: builder.Services.AddAzureBlobStorage(new AzureBlobStorageOptions(), connectionString);

builder.Services.AddS3Storage(new S3StorageOptions { BucketName = "my-app-uploads" });          // needs an IAmazonS3
builder.Services.AddGoogleCloudStorage(new GoogleCloudStorageOptions { BucketName = "my-app-uploads" }); // needs a StorageClient
builder.Services.AddSftpStorage(new SftpStorageOptions { Host = "sftp.example.com", Username = "app", PrivateKeyPath = "/secrets/id_rsa" });
builder.Services.AddFtpStorage(new FtpStorageOptions { Host = "ftp.example.com", Username = "app", Password = "secret", UseSsl = true });
```

`UseStorage<AzureBlobFileStorage>()` (the type-based overload) lets the DI container construct the
provider — it works only if every constructor dependency (`BlobServiceClient` and
`AzureBlobStorageOptions`, or `IAmazonS3` and `S3StorageOptions`) is registered separately. The
factory overload above builds everything in one place. Runnable setup samples:
`samples/Pragmatic.Storage.Samples/AzureBlobStorageSample.cs` and `S3StorageSample.cs`.

See [Custom Providers](custom-providers.md) for the full options of every shipped provider
(Google Cloud, SFTP, FTP/FTPS).

## Step 2: Create an Upload Action

### With Pragmatic.Actions

```csharp
public class UploadDocument : DomainAction<DocumentDto>
{
    private IFileStorage _storage = null!;
    private IRepository<Document> _repository = null!;

    public required IFormFile File { get; init; }
    public required string Category { get; init; }

    public override async Task<Result<DocumentDto, IError>> Execute(CancellationToken ct = default)
    {
        // Validate file
        if (File.Length == 0)
            return ValidationError.For("File", "validation.notempty");

        if (File.Length > 10 * 1024 * 1024)
            return ValidationError.For("File", "upload.too_large", ("maxMegabytes", 10));

        // Save to storage
        await using var stream = File.OpenReadStream();
        var uri = await _storage.SaveAsync(stream, File.FileName, Category, ct);

        // Create domain entity
        var document = Document.Create(File.FileName, uri, File.Length, Category);
        await _repository.AddAsync(document, ct);
        await _repository.SaveAsync(ct);

        return document.ToDto();
    }
}
```

### With Minimal API

```csharp
app.MapPost("/api/files", async (IFormFile file, string container, IFileStorage storage) =>
{
    await using var stream = file.OpenReadStream();
    var uri = await storage.SaveAsync(stream, file.FileName, container);
    return Results.Created(uri.ToString(), new { uri });
});
```

## Result-Based Contract

Every `IFileStorage` method has a `*AsResultAsync` counterpart that returns `Result<T, IError>`
(`VoidResult<IError>` for delete) instead of throwing. Reach for it on the framework's
action/mutation path: the failure is a value that composes with the rest of the pipeline — no
`try`/`catch`, and the error carries the right HTTP status.

```csharp
public partial class UploadPhoto : DomainAction<Uri>
{
    private IFileStorage _storage = null!;
    public required IFormFile File { get; init; }

    public override async Task<Result<Uri, IError>> Execute(CancellationToken ct = default)
    {
        await using var stream = File.OpenReadStream();
        return await _storage.SaveAsResultAsync(stream, File.FileName, "photos", ct);
    }
}
```

| Method | Returns | Failure |
|--------|---------|---------|
| `SaveAsResultAsync(stream, fileName, container, ct)` | `Result<Uri, IError>` | `FileTooLargeError` (413), `StorageWriteError` (500) |
| `GetAsResultAsync(uri, ct)` | `Result<Stream, IError>` | `StorageFileNotFoundError` (404), `StorageWriteError` (500) |
| `ExistsAsResultAsync(uri, ct)` | `Result<bool, IError>` | `StorageWriteError` (500) — a missing file is a successful `false` |
| `DeleteAsResultAsync(uri, ct)` | `VoidResult<IError>` | `StorageWriteError` (500) |

`StorageWriteError` is transient-aware (`IsTransient` is set for `IOException` / `TimeoutException`),
so a resilience policy can retry on it. Cancellation is never swallowed — an
`OperationCanceledException` propagates rather than becoming a failure.

**Use the throwing surface** (`SaveAsync` and friends) for direct, non-pipeline use — a background
utility, a script, a place where you already handle exceptions. The throwing path signals an
oversized upload with `FileSizeLimitExceededException` (a subtype of `InvalidOperationException`,
so existing `catch (InvalidOperationException)` code keeps working).

## Step 3: Read a File Back

`GetAsync` opens a read stream for a stored file (`null` if it does not exist); `ExistsAsync`
checks presence without opening it. Together with save and delete, the full round-trip:

```csharp
Uri uri = await storage.SaveAsync(input, "report.pdf", "documents", ct);

await using Stream? stream = await storage.GetAsync(uri, ct);   // null if missing; caller disposes
bool exists = await storage.ExistsAsync(uri, ct);               // true

await storage.DeleteAsync(uri, ct);                             // idempotent — no-op if already gone
```

A download endpoint that streams the file to the client:

```csharp
app.MapGet("/api/documents/{id:guid}/file", async (
    Guid id, IRepository<Document> repository, IFileStorage storage, CancellationToken ct) =>
{
    var document = await repository.GetByIdAsync(id, ct);
    if (document is null)
        return Results.NotFound();

    var stream = await storage.GetAsync(document.FileUri, ct);
    return stream is null
        ? Results.NotFound()
        : Results.Stream(stream, MimeTypes.GetMimeType(Path.GetExtension(document.FileName)));
});
```

Always pass back the exact `Uri` returned by `SaveAsync`: the providers validate it (LocalDisk
rejects paths outside the storage root, Azure rejects foreign account hosts, S3 accepts only
`s3://` or `PublicBaseUrl`-prefixed URIs).

## Step 4: Create a Delete Action

```csharp
public class DeleteDocument : VoidDomainAction
{
    private IFileStorage _storage = null!;
    private IRepository<Document> _repository = null!;

    public required Guid DocumentId { get; init; }

    public override async Task<VoidResult> Execute(CancellationToken ct = default)
    {
        var document = await _repository.GetByIdAsync(DocumentId, ct);
        if (document is null)
            return NotFoundError.For("Document", DocumentId);

        // Delete from storage
        await _storage.DeleteAsync(document.FileUri, ct);

        // Delete from database
        await _repository.RemoveAsync(document, ct);
        await _repository.SaveAsync(ct);

        return VoidResult.Success();
    }
}
```

## Step 5: Serve Files

For local disk storage, files are stored under `wwwroot/files/`. Enable static file serving:

```csharp
app.UseStaticFiles();
```

URIs returned by `LocalDiskFileStorage` are relative (e.g., `/files/photos/abc123.jpg`) and map directly to the static files path.

For cloud storage, `SaveAsync` returns absolute URIs. Whether clients can open them directly
depends on your configuration: Azure returns the blob URI
(`https://account.blob.core.windows.net/photos/abc123.jpg`) — directly accessible only if the
container allows public read, otherwise serve through your API (`GetAsync`) or attach a SAS token.
S3 returns a `PublicBaseUrl`-based URI (CDN or website endpoint) when that option is set, otherwise
an `s3://` URI intended to be read back through `GetAsync`.

## File Metadata and Signed URLs

Two optional capabilities live next to `IFileStorage`. A provider that supports them implements the
extra interface; `IFileStorage` itself stays at four methods, so pattern-match to use them.

### File metadata — `IFileInfoProvider`

`GetInfoAsync(uri)` returns a `StoredFileInfo` (`SizeBytes`, `ContentType`, `LastModified`,
`FileUri`) without downloading the file — a local `stat`, an Azure `GetProperties`, an S3 `HEAD`.
It returns `null` for a missing file, exactly like `GetAsync`. Every shipped provider implements it.

```csharp
if (storage is IFileInfoProvider info)
{
    StoredFileInfo? meta = await info.GetInfoAsync(document.FileUri, ct);
    if (meta is not null)
        Console.WriteLine($"{meta.SizeBytes} bytes, {meta.ContentType}, {meta.LastModified}");
}
```

### Signed download URLs — `ISignedUrlProvider`

`GetDownloadUrlAsync(uri, expiry)` mints a temporary, pre-authenticated URL so the browser downloads
a **private** file straight from the backend — the application never proxies the bytes. Azure (SAS),
S3/R2 (pre-signed), and Google Cloud (signed URL) implement it; `LocalDiskFileStorage`,
`InMemoryFileStorage`, SFTP, and FTP do not (local files are served as static files).

```csharp
app.MapGet("/api/documents/{id:guid}/download", async (
    Guid id, IRepository<Document> repository, IFileStorage storage, CancellationToken ct) =>
{
    var document = await repository.GetByIdAsync(id, ct);
    if (document is null)
        return Results.NotFound();

    if (storage is ISignedUrlProvider signer)
    {
        var url = await signer.GetDownloadUrlAsync(document.FileUri, TimeSpan.FromMinutes(15), ct);
        return Results.Redirect(url.ToString());   // browser fetches the blob directly
    }

    // Fallback for providers without signed URLs: stream through the API
    var stream = await storage.GetAsync(document.FileUri, ct);
    return stream is null ? Results.NotFound() : Results.Stream(stream);
});
```

On Azure, a SAS URL requires the `BlobServiceClient` to be created with a shared-key credential (a
connection string or an account key). A client built from a managed identity cannot sign — the call
throws `NotSupportedException`. On Google Cloud, signing requires a service-account credential
(supply a `UrlSigner` in the options or set `GOOGLE_APPLICATION_CREDENTIALS`).

Both capabilities also have Result-based counterparts — `GetInfoAsResultAsync(uri)` →
`Result<StoredFileInfo, IError>` and `GetDownloadUrlAsResultAsync(uri, expiry)` → `Result<Uri, IError>`
— for the action/mutation path.

## Step 6: Display in Frontend

The `Uri` stored on your entity works as a direct download URL:

```html
<!-- For local disk: relative URI -->
<img src="/files/photos/abc123.jpg" />

<!-- For cloud: absolute URI (public container/bucket or CDN — see Step 5) -->
<img src="https://account.blob.core.windows.net/photos/abc123.jpg" />
```

## Container Conventions

Use descriptive container names that match your domain:

| Container | Content |
|-----------|---------|
| `"photos"` | User-uploaded images |
| `"documents"` | PDF/Word documents |
| `"imports"` | CSV/Excel import files |
| `"invoices"` | Generated invoice PDFs |
| `"avatars"` | User profile pictures |

Containers map to folders (local disk) or blob containers (Azure) or prefixes (S3).

## Testing

Reference the `Pragmatic.Storage.InMemory` package and register `InMemoryFileStorage` — it backs all
four `IFileStorage` methods (and `IFileInfoProvider`) with a dictionary, no filesystem or network:

```csharp
using Pragmatic.Storage.InMemory;

services.AddInMemoryStorage();
```

`AddInMemoryStorage()` registers `InMemoryFileStorage` as the `IFileStorage` singleton. Every
`SaveAsync` returns a resolvable URI, `GetAsync`/`ExistsAsync` round-trip it, and `DeleteAsync` is
idempotent — so an upload endpoint under test behaves exactly as it would in production, without
touching disk or a cloud account. Use the same package for a zero-config local run.

## Environment-Based Configuration

A common pattern is to switch provider based on the environment:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    if (app.Environment.IsDevelopment())
    {
        app.UseStorage(sp => new LocalDiskFileStorage(
            Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
            sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
            maxFileSizeBytes: 10 * 1024 * 1024));
    }
    else
    {
        app.UseStorage(sp => new AzureBlobFileStorage(
            new BlobServiceClient(app.Configuration.GetConnectionString("BlobStorage")),
            new AzureBlobStorageOptions(),
            sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));
    }
});
```
