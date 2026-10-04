# Architecture and Core Concepts

This guide explains **why** Pragmatic.Storage exists, how its pieces fit together, and how to choose the right provider for each environment. Read this before diving into the getting-started guide.

---

## The Problem

Every non-trivial application stores files -- user avatars, PDF invoices, CSV imports, document attachments. The storage backend varies by environment: local disk in development, Azure Blob in staging, S3 in production. Without an abstraction, file storage logic leaks into business code.

### Direct provider coupling: vendor lock-in at the domain level

```csharp
public class UploadDocument : DomainAction<DocumentDto>
{
    private readonly BlobServiceClient _blobClient;  // Azure SDK dependency

    public required IFormFile File { get; init; }

    public override async Task<Result<DocumentDto, IError>> Execute(CancellationToken ct)
    {
        var container = _blobClient.GetBlobContainerClient("documents");
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobName = $"{Guid.NewGuid():N}{Path.GetExtension(File.FileName)}";
        var blob = container.GetBlobClient(blobName);

        await using var stream = File.OpenReadStream();
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);

        var document = Document.Create(File.FileName, blob.Uri, File.Length, "documents");
        // ...
    }
}
```

The domain action is now coupled to `Azure.Storage.Blobs`. Switching to S3 requires rewriting every action that touches files. Running locally requires an Azure Storage emulator (Azurite) or conditional logic branches. Unit testing requires mocking the entire Azure SDK surface.

### Conditional branching: environment-specific code in business logic

```csharp
public override async Task<Result<DocumentDto, IError>> Execute(CancellationToken ct)
{
    Uri fileUri;

    if (_environment.IsDevelopment())
    {
        // Local disk
        var dir = Path.Combine(_webRoot, "files", "documents");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
        await using var fs = System.IO.File.Create(path);
        await stream.CopyToAsync(fs, ct);
        fileUri = new Uri($"/files/documents/{Path.GetFileName(path)}", UriKind.Relative);
    }
    else if (_config["Storage:Provider"] == "Azure")
    {
        // Azure Blob
        // ...
    }
    else
    {
        // S3
        // ...
    }
}
```

Three implementations interleaved with business logic. Each new provider adds another branch. The action's cyclomatic complexity grows with the number of storage backends, and testing requires covering every branch.

### The fundamental issue

File storage is an infrastructure concern. The business logic needs exactly four operations: save a file (get back a URI), read it back as a stream, check whether it exists, and delete it. The physical location -- local disk, Azure, S3, MinIO -- is a deployment decision that should be made at composition time, not in domain code.

---

## The Solution

Pragmatic.Storage defines a minimal `IFileStorage` interface with four methods covering the full lifecycle of a stored file. Domain actions depend on the abstraction; the provider is selected once at startup via `IPragmaticBuilder` or DI registration.

```csharp
public interface IFileStorage
{
    Task<Uri> SaveAsync(
        Stream content,
        string fileName,
        string container,
        CancellationToken ct = default);

    Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default);

    Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default);

    Task DeleteAsync(Uri fileUri, CancellationToken ct = default);
}
```

The same upload action, decoupled from the provider:

```csharp
public class UploadDocument : DomainAction<DocumentDto>
{
    private IFileStorage _storage = null!;  // Injected by Actions SG

    public required IFormFile File { get; init; }

    public override async Task<Result<DocumentDto, IError>> Execute(CancellationToken ct)
    {
        await using var stream = File.OpenReadStream();
        var uri = await _storage.SaveAsync(stream, File.FileName, "documents", ct);

        var document = Document.Create(File.FileName, uri, File.Length, "documents");
        // ...
    }
}
```

The provider is selected once in `Program.cs`:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    // Development: local disk
    app.UseStorage(sp => new LocalDiskFileStorage(
        basePath,
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
});
```

Swap to Azure for production -- the provider takes the SDK client and its options directly:

```csharp
app.UseStorage(sp => new AzureBlobFileStorage(
    new BlobServiceClient(connectionString),
    new AzureBlobStorageOptions { ContainerPrefix = "myapp-" },
    sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));
```

Zero changes to the domain action. Zero conditional branches. The business logic is identical regardless of whether files land on a local disk or in a cloud bucket.

---

## IFileStorage Interface

The core abstraction has four methods. Every file storage operation reduces to "save content and get a URI", "read content by URI", "check existence by URI", or "delete content by URI."

### SaveAsync

```csharp
Task<Uri> SaveAsync(
    Stream content,
    string fileName,
    string container,
    CancellationToken ct = default);
```

| Parameter | Description |
|-----------|-------------|
| `content` | The file content as a `Stream`. Typically from `IFormFile.OpenReadStream()`. |
| `fileName` | Original file name. Used for extension and content-type detection. The provider does not use this as the storage name -- files are stored with unique names (GUIDs) to prevent collisions. |
| `container` | Logical grouping (e.g., `"photos"`, `"invoices"`, `"imports"`). Maps to folders (local disk), blob containers (Azure), or key prefixes (S3). Keep container names flat -- see [Container Conventions](#container-conventions). |

**Returns:** A `Uri` pointing to the stored file. The URI shape depends on the provider:

| Provider | URI Type | Example |
|----------|----------|---------|
| LocalDiskFileStorage | Relative | `/files/photos/a1b2c3d4e5f6.jpg` |
| Azure Blob | Absolute blob URI | `https://account.blob.core.windows.net/myapp-photos/a1b2c3d4.jpg` |
| S3 | `s3://` or public URL | `s3://bucket/photos/a1b2c3d4.jpg`, or `https://cdn.example.com/uploads/photos/a1b2c3d4.jpg` when `PublicBaseUrl` is configured |

The URI identifies the file for the other three methods. Whether a browser can fetch it directly depends on the provider: LocalDisk URIs resolve through static file middleware; Azure blob URIs resolve only if the container allows public access or the URL carries a SAS token; S3 URIs resolve through `PublicBaseUrl` (CDN or website endpoint) -- `s3://` URIs are storage identifiers, not HTTP URLs. For private files, stream them to the client through `GetAsync` instead.

### GetAsync

```csharp
Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default);
```

| Parameter | Description |
|-----------|-------------|
| `fileUri` | The URI returned by `SaveAsync`. |

**Returns:** A readable stream, or `null` if the file does not exist.

The caller owns the returned stream -- dispose it with `await using`. Cloud providers return a wrapper stream whose `Dispose` also releases the underlying HTTP connection, so disposal is mandatory, not optional:

```csharp
await using var stream = await _storage.GetAsync(entity.FileUri, ct);
if (stream is null)
    return DocumentErrors.FileMissing(entity.Id);

await stream.CopyToAsync(destination, ct);
```

On `LocalDiskFileStorage`, the cancellation token is checked before the file handle opens; once the handle is open, the returned `FileStream` reads synchronously and does not observe the token.

### ExistsAsync

```csharp
Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default);
```

Returns `true` if a file exists at the URI. Use it to validate stale references (e.g., an entity whose file was removed by an external process) without opening a stream.

### DeleteAsync

```csharp
Task DeleteAsync(Uri fileUri, CancellationToken ct = default);
```

| Parameter | Description |
|-----------|-------------|
| `fileUri` | The URI returned by `SaveAsync`. The provider uses this to locate and delete the file. |

Deleting a non-existent file is a no-op (does not throw), on every provider. This makes cleanup operations idempotent and safe to retry.

### Result-based surface

Each method has a `*AsResultAsync` counterpart that returns `Result<T, IError>` (`VoidResult<IError>` for delete) instead of throwing. The error slot is `IError` (matching the Actions/Mutation path that returns `Result<T, IError>`), and the concrete error types convert implicitly:

```csharp
public interface IFileStorage { /* the four throwing methods above */ }

// Extension surface (namespace Pragmatic.Storage), typed errors instead of exceptions:
Task<Result<Uri, IError>>    SaveAsResultAsync(Stream content, string fileName, string container, CancellationToken ct = default);
Task<Result<Stream, IError>> GetAsResultAsync(Uri fileUri, CancellationToken ct = default);
Task<Result<bool, IError>>   ExistsAsResultAsync(Uri fileUri, CancellationToken ct = default);
Task<VoidResult<IError>>     DeleteAsResultAsync(Uri fileUri, CancellationToken ct = default);
```

| Error | Code | HTTP | When |
|-------|------|------|------|
| `FileTooLargeError` | `FILE_TOO_LARGE` | 413 | Upload exceeds the configured `maxFileSizeBytes` (carries `LimitBytes` / `ActualBytes`). |
| `StorageFileNotFoundError` | `STORAGE_FILE_NOT_FOUND` | 404 | `GetAsResultAsync` on a missing file (a `null` stream becomes this error). |
| `StorageWriteError` | `STORAGE_WRITE_ERROR` | 500 | A provider I/O fault or invalid container/path. Transient-aware: `IsTransient` is set for `IOException` / `TimeoutException`, so a resilience policy can retry. |

**Which surface?** Use `*AsResultAsync` in the framework's action/mutation path: the failure is a value that composes with the rest of the pipeline, no `try`/`catch`, and the error already carries the right status. Use the throwing `IFileStorage` methods for direct, out-of-pipeline use where you handle exceptions yourself; the throwing path signals an oversized upload with `FileSizeLimitExceededException` (a subtype of `InvalidOperationException`). An `OperationCanceledException` is never converted to a failure. It propagates from both surfaces, keeping cancellation distinct from an I/O error.

### Optional capabilities

Two optional interfaces sit next to `IFileStorage`. It stays at four methods; a provider that can do more implements the extra interface, and callers pattern-match to reach it (`if (storage is IFileInfoProvider p)` / `is ISignedUrlProvider s`).

| Capability | Method | Providers |
|------------|--------|-----------|
| `IFileInfoProvider` | `GetInfoAsync(uri)` → `StoredFileInfo?` (`SizeBytes`, `ContentType`, `LastModified`, `FileUri`); `null` if missing, like `GetAsync` | every shipped provider |
| `ISignedUrlProvider` | `GetDownloadUrlAsync(uri, expiry)` → `Uri` (a temporary, pre-authenticated download URL) | Azure (SAS), S3/R2 (pre-signed), Google Cloud (signed URL); **not** LocalDisk, InMemory, SFTP, FTP |

A signed URL lets a browser download a private file straight from the backend, so the application never proxies the bytes. Result-based counterparts exist too: `GetInfoAsResultAsync(uri)` → `Result<StoredFileInfo, IError>` and `GetDownloadUrlAsResultAsync(uri, expiry)` → `Result<Uri, IError>`. See [Getting Started](getting-started.md#file-metadata-and-signed-urls) for the endpoint pattern and the Azure/Google credential requirements.

### Design Decisions

**Why only four methods?** Save, read, exists, delete cover the lifecycle of a stored file. `GetAsync` exists for server-side consumers -- streaming private files to authorized users, post-processing uploads, and the Pragmatic.Messaging claim-check pattern, which stores large message payloads through `SaveAsync` and reads them back through `GetAsync`. Public files are still served by the URI directly (static files, CDN, presigned URL) without touching the interface. Listing files, moving files, and metadata queries are provider-specific concerns that do not belong in a domain-level abstraction.

**Why `Stream` instead of `byte[]`?** Streams avoid loading the entire file into memory. For large files (uploads > 100 MB), this is critical. The `content` stream is read once and written to the destination. Callers must not assume the stream is seekable.

**Why `Uri` instead of `string`?** URIs enforce a structured format and distinguish relative from absolute paths. A `string` return value invites inconsistency ("files/photos/abc.jpg" vs. "/files/photos/abc.jpg" vs. "https://...").

---

## Providers

### LocalDiskFileStorage (built-in)

The built-in implementation for development and demo environments. Ships with the `Pragmatic.Storage` package.

#### Storage Layout

Files are saved to `{basePath}/files/{container}/{guid}{extension}`:

```
wwwroot/
  files/
    photos/
      a1b2c3d4e5f6.jpg
      f7a8b9c0d1e2.png
    invoices/
      9e8d7c6b5a4f.pdf
```

| Behavior | Detail |
|----------|--------|
| GUID naming | Prevents filename collisions. Each file is stored as `Guid.NewGuid():N` plus the original extension. |
| Returns relative URIs | e.g., `/files/photos/a1b2c3d4e5f6.jpg` |
| Creates directories | `{basePath}/files/{container}/` is created automatically if it does not exist. |
| Size limit | With `maxFileSizeBytes > 0`, seekable streams are rejected up front via `Length`; non-seekable streams are rejected mid-copy once the running total exceeds the limit, and the partial file is removed. |
| Failure cleanup | If the write fails for any reason, the partial file is deleted (best effort) before the exception propagates. |
| Path-traversal protection (write) | The `container` argument is resolved against `{basePath}/files/` and must stay inside it. Traversal segments or absolute paths throw `ArgumentException`. |
| Path-traversal protection (read/delete) | `GetAsync`/`ExistsAsync`/`DeleteAsync` resolve the URI safely against `basePath`. Rooted paths, UNC paths, and traversal segments that escape the root are treated exactly like a missing file: `null` / `false` / no-op. A rejected path is indistinguishable from a not-found -- by design, so probing callers learn nothing about the filesystem. |
| Idempotent delete | `DeleteAsync` checks `File.Exists` and silently skips missing files. |
| Thread safety | Stateless; safe as a singleton. |

#### Constructor

```csharp
public LocalDiskFileStorage(
    string basePath,
    ILogger<LocalDiskFileStorage> logger,
    long maxFileSizeBytes = 0)
```

| Parameter | Description |
|-----------|-------------|
| `basePath` | Root directory (e.g., `IHostEnvironment.ContentRootPath + "/wwwroot"`). A `files/` subdirectory is created automatically. |
| `logger` | Logger for save/delete operations. Logs the original file name and stored path. |
| `maxFileSizeBytes` | Maximum accepted size of a single uploaded file, in bytes. `0` (default) means no limit. Oversized uploads throw `InvalidOperationException`. |

`AddLocalDiskStorage(basePath)` registers the provider without a size limit. To set `maxFileSizeBytes`, use factory registration:

```csharp
app.UseStorage(sp => new LocalDiskFileStorage(
    basePath,
    sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
    maxFileSizeBytes: 10 * 1024 * 1024));
```

#### Serving Files

Combine with ASP.NET Core's static file middleware to serve stored files:

```csharp
app.UseStaticFiles();  // Serves from wwwroot/ by default
```

The relative URIs returned by `SaveAsync` (e.g., `/files/photos/abc.jpg`) map directly to the static files path (`wwwroot/files/photos/abc.jpg`).

### Pragmatic.Storage.Azure

Azure Blob Storage provider. Each logical container maps to a blob container named `{ContainerPrefix}{container}`.

```csharp
public sealed class AzureBlobFileStorage(
    BlobServiceClient blobService,
    AzureBlobStorageOptions options,
    ILogger<AzureBlobFileStorage> logger) : IFileStorage
```

| Option | Description |
|--------|-------------|
| `ContainerPrefix` | Prefix for blob container names (e.g., `"myapp-"` → `"myapp-photos"`). Default: empty. |
| `MaxFileSizeBytes` | Maximum accepted upload size in bytes. `0` (default) means no limit; a positive value rejects oversized uploads (up front for seekable streams, mid-transfer otherwise). |

| Behavior | Detail |
|----------|--------|
| Container creation | `SaveAsync` calls `CreateIfNotExistsAsync` the first time it sees a container name; subsequent saves skip the round-trip. |
| Content type | Set on the blob from the file extension via `MimeTypes.GetMimeType`. |
| Returned URI | The absolute blob URI. Directly fetchable only if the container allows public access or you append a SAS token; otherwise serve through `GetAsync`. |
| URI validation | `GetAsync`/`ExistsAsync`/`DeleteAsync` verify that the URI host matches the configured storage account and throw `ArgumentException` on mismatch -- callers cannot use the provider to reach blobs in arbitrary accounts. |
| Get stream | Wraps the Azure download result; disposing the stream releases the HTTP connection. |
| Idempotent delete | Uses `DeleteIfExistsAsync`. |

Metadata (`IFileInfoProvider.GetInfoAsync`, via `GetProperties`) and signed URLs
(`ISignedUrlProvider.GetDownloadUrlAsync`, a read-only SAS) are both supported. A SAS URL requires the
`BlobServiceClient` to be built with a shared-key credential (a connection string or account key); a
client built from a managed identity throws `NotSupportedException` when asked to sign.

Registration -- either construct the provider through `UseStorage`, or use the `AddAzureBlobStorage`
DI helper (a connection-string overload registers the `BlobServiceClient` for you):

```csharp
// Via IPragmaticBuilder: factory registration
app.UseStorage(sp => new AzureBlobFileStorage(
    new BlobServiceClient(builder.Configuration.GetConnectionString("Storage")),
    new AzureBlobStorageOptions { ContainerPrefix = "myapp-" },
    sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));

// Via DI helper: pass a connection string and the client is registered too
builder.Services.AddAzureBlobStorage(
    new AzureBlobStorageOptions { ContainerPrefix = "myapp-", MaxFileSizeBytes = 10 * 1024 * 1024 },
    builder.Configuration.GetConnectionString("Storage")!);
```

### Pragmatic.Storage.S3

Amazon S3 / S3-compatible provider: AWS, Cloudflare R2, MinIO, Wasabi, DigitalOcean Spaces, Backblaze B2. Files are stored as objects with key `{KeyPrefix}{container}/{guid}{extension}` in a single bucket.

```csharp
public sealed class S3FileStorage(
    IAmazonS3 s3,
    S3StorageOptions options,
    ILogger<S3FileStorage> logger) : IFileStorage
```

| Option | Description |
|--------|-------------|
| `BucketName` | **Required.** The bucket all objects are stored in. The bucket must already exist. |
| `KeyPrefix` | Optional key prefix (e.g., `"uploads/"`). Default: empty. |
| `PublicBaseUrl` | Public base URL (CDN or S3 website endpoint). When set, `SaveAsync` returns `{PublicBaseUrl}/{key}` URIs; when `null`, it returns `s3://bucket/key` URIs. |
| `MaxFileSizeBytes` | Maximum accepted upload size in bytes. `0` (default) means no limit. |

| Behavior | Detail |
|----------|--------|
| Size limit | Seekable streams are rejected up front via `Length`; non-seekable streams are wrapped in `LimitedReadStream`, which throws once the running byte total exceeds the limit while the SDK consumes the payload. |
| Content type | Set on the object from the file extension via `MimeTypes.GetMimeType`. |
| URI resolution | `GetAsync`/`ExistsAsync`/`DeleteAsync` accept only `s3://` URIs or URIs prefixed with the configured `PublicBaseUrl`; anything else throws `ArgumentException` -- callers cannot inject arbitrary object keys. |
| Get stream | Wraps the S3 response; disposing the stream releases the HTTP connection. `GetAsync` returns `null` on 404. |
| Idempotent delete | 404 / `NoSuchKey` responses are swallowed. |

Metadata (`IFileInfoProvider`, via `GetObjectMetadata`) and signed URLs (`ISignedUrlProvider`, via the SDK's pre-signed GET) are both supported.

Registration -- either construct the provider through `UseStorage`, or register an `IAmazonS3` client and use the `AddS3Storage` DI helper:

```csharp
app.UseStorage(sp => new S3FileStorage(
    new AmazonS3Client(),  // region + credentials from the AWS SDK configuration chain
    new S3StorageOptions
    {
        BucketName = "my-app-uploads",
        KeyPrefix = "uploads/",
        PublicBaseUrl = "https://cdn.example.com/uploads",
        MaxFileSizeBytes = 10 * 1024 * 1024,
    },
    sp.GetRequiredService<ILogger<S3FileStorage>>()));

// Or via DI helper: register the client, then the provider
builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client());
builder.Services.AddS3Storage(new S3StorageOptions { BucketName = "my-app-uploads" });
```

For MinIO, Cloudflare R2, Wasabi, DigitalOcean Spaces, or Backblaze B2, point the client at the endpoint: `new AmazonS3Client(credentials, new AmazonS3Config { ServiceURL = "https://<endpoint>" })`.

### Pragmatic.Storage.GoogleCloud

Google Cloud Storage provider. Files are stored as objects with name `{ObjectPrefix}{container}/{guid}{extension}` in a single bucket. Implements `IFileStorage`, `IFileInfoProvider`, and `ISignedUrlProvider`.

```csharp
public sealed class GoogleCloudFileStorage(
    StorageClient storageClient,
    GoogleCloudStorageOptions options,
    ILogger<GoogleCloudFileStorage> logger) : IFileStorage, IFileInfoProvider, ISignedUrlProvider
```

| Option | Description |
|--------|-------------|
| `BucketName` | **Required.** The target bucket -- it must already exist. |
| `ObjectPrefix` | Optional object-name prefix (e.g., `"uploads/"`). Default: empty. |
| `PublicBaseUrl` | When set, `SaveAsync` returns `{PublicBaseUrl}/{object}` URIs; when `null`, it returns `gs://bucket/object` URIs. |
| `MaxFileSizeBytes` | Maximum accepted upload size in bytes. `0` (default) means no limit. |
| `UrlSigner` | Optional `UrlSigner` used for signed URLs. Signing needs a service-account credential (a private key), which the plain `StorageClient` does not carry: supply a `UrlSigner` here or set `GOOGLE_APPLICATION_CREDENTIALS`; otherwise `GetDownloadUrlAsync` throws `NotSupportedException`. |

The application registers the `StorageClient` (typically `StorageClient.Create()`, which picks up application-default credentials); `AddGoogleCloudStorage(options)` resolves it from the container:

```csharp
builder.Services.AddSingleton(StorageClient.Create());
builder.Services.AddGoogleCloudStorage(new GoogleCloudStorageOptions
{
    BucketName = "my-app-uploads",
    MaxFileSizeBytes = 10 * 1024 * 1024,
});
```

### Pragmatic.Storage.Sftp

SFTP (SSH File Transfer Protocol) provider. A connection is opened per operation. Implements `IFileStorage` and `IFileInfoProvider` -- no signed URLs. A stored file lives at `{BasePath}/{container}/{guid}{extension}`.

| Option | Description |
|--------|-------------|
| `Host` | **Required.** SFTP server host name or IP. |
| `Port` | SSH port. Default: `22`. |
| `Username` | **Required.** Authentication user name. |
| `Password` | Password for password authentication. Supply this **or** `PrivateKeyPath`. |
| `PrivateKeyPath` | Path to a private key file (OpenSSH / PEM) for public-key authentication. |
| `PrivateKeyPassphrase` | Passphrase for an encrypted private key, or `null`. |
| `BasePath` | Remote root directory. Default: `"/"`. |
| `MaxFileSizeBytes` | Maximum accepted upload size in bytes. `0` (default) means no limit. |

```csharp
builder.Services.AddSftpStorage(new SftpStorageOptions
{
    Host = "sftp.example.com",
    Username = "app",
    PrivateKeyPath = "/secrets/id_rsa",
    BasePath = "/uploads",
    MaxFileSizeBytes = 10 * 1024 * 1024,
});
```

### Pragmatic.Storage.Ftp

FTP / FTPS provider (FluentFTP). Enable `UseSsl` for explicit FTPS (FTP over TLS); the server certificate is validated against the system trust store. Implements `IFileStorage` and `IFileInfoProvider` -- no signed URLs.

| Option | Description |
|--------|-------------|
| `Host` | **Required.** FTP server host name or IP. |
| `Port` | FTP control port. Default: `21`. |
| `Username` | **Required.** Authentication user name. |
| `Password` | Password, or `null` for anonymous login. |
| `UseSsl` | `true` for explicit FTPS (FTP over TLS). Default: `false`. |
| `BasePath` | Root directory on the server. Default: `"/"`. |
| `MaxFileSizeBytes` | Maximum accepted upload size in bytes. `0` (default) means no limit. |

```csharp
builder.Services.AddFtpStorage(new FtpStorageOptions
{
    Host = "ftp.example.com",
    Username = "app",
    Password = "secret",
    UseSsl = true,
    BasePath = "/data",
    MaxFileSizeBytes = 10 * 1024 * 1024,
});
```

### Pragmatic.Storage.InMemory

`InMemoryFileStorage` keeps files in a dictionary, for tests and local development, with no filesystem or network. Implements `IFileStorage` and `IFileInfoProvider`.

```csharp
builder.Services.AddInMemoryStorage();
```

### Custom Providers

Implement `IFileStorage` for any other backend (a database, a proprietary DMS). See [Custom Providers](custom-providers.md) for the full contract and a working example.

---

## File Naming and Collisions

All providers follow the same naming strategy:

1. The original file name is **not** used as the storage name.
2. A GUID (`Guid.NewGuid():N`) provides a unique, collision-free name.
3. The original file's extension is preserved for content-type detection and display.

This means `report.pdf` is stored as `a1b2c3d4e5f67890a1b2c3d4e5f67890.pdf`. Two users uploading files named `photo.jpg` at the same time get different storage names.

---

## Content Types

`MimeTypes.GetMimeType(extension)` maps a file extension to a MIME type. The Azure and S3 providers use it to set the content type on stored objects.

```csharp
MimeTypes.GetMimeType(".jpg");   // "image/jpeg"
MimeTypes.GetMimeType("png");    // "image/png" -- leading dot optional
MimeTypes.GetMimeType(".xyz");   // "application/octet-stream" -- unknown extensions fall back
MimeTypes.GetMimeType(null);     // "application/octet-stream"
```

The known set covers common web and office formats: images (`jpg`/`jpeg`, `png`, `gif`, `webp`, `svg`), documents (`pdf`, `doc`, `docx`, `xls`, `xlsx`, `csv`, `txt`), data (`json`, `xml`, `zip`), and media (`mp4`, `mp3`). Matching is case-insensitive.

**Security note:** the content type is derived from the extension of the caller-supplied `fileName` -- there is no content sniffing. A file named `photo.jpg` gets `image/jpeg` regardless of what its bytes contain. Treat the stored content type as untrusted metadata: validate uploads at the endpoint (e.g., `[AllowedContentTypes]`) and send `X-Content-Type-Options: nosniff` when serving user-uploaded files.

---

## Container Conventions

The `container` parameter groups files logically. Use descriptive names that match your domain:

| Container | Content |
|-----------|---------|
| `"photos"` | User-uploaded images |
| `"documents"` | PDF/Word documents |
| `"imports"` | CSV/Excel import files |
| `"invoices"` | Generated invoice PDFs |
| `"avatars"` | User profile pictures |
| `"product-photos"` | Product catalog images |

Containers map to different physical structures depending on the provider:

| Provider | Container Maps To |
|----------|------------------|
| LocalDisk | Filesystem directory: `files/{container}/` |
| Azure Blob | Blob container named `{ContainerPrefix}{container}` |
| S3 | Key prefix: `{KeyPrefix}{container}/` within the bucket |
| Google Cloud | Object-name prefix: `{ObjectPrefix}{container}/` within the bucket |
| SFTP / FTP | Directory under `{BasePath}/{container}/` |

**Container names must be flat: no `/`.** The strict provider is Azure: blob container names allow only lowercase letters, digits, and hyphens, so a container like `"photos/2024"` works on local disk, S3, and Google Cloud (where the container is just a key/path segment) but **fails on Azure**. Keeping names flat and lowercase (`"product-photos"`, not `"products/photos"`) makes your code portable across every provider. Encode hierarchy in the name with hyphens, not slashes.

---

## DI Registration

### StorageServiceCollectionExtensions

Direct `IServiceCollection` registration for scenarios where `IPragmaticBuilder` is not available:

```csharp
// LocalDiskFileStorage as singleton: no size limit, or with a cap
services.AddLocalDiskStorage(basePath);
services.AddLocalDiskStorage(basePath, maxFileSizeBytes: 10 * 1024 * 1024);

// InMemoryFileStorage for tests / local dev (Pragmatic.Storage.InMemory)
services.AddInMemoryStorage();

// Provider helpers: register the SDK client, then the provider
services.AddSingleton(new BlobServiceClient(connectionString));
services.AddAzureBlobStorage(new AzureBlobStorageOptions { ContainerPrefix = "myapp-" });
// ...also AddS3Storage, AddGoogleCloudStorage, AddSftpStorage, AddFtpStorage

// Register any implementation as singleton; constructor dependencies resolve from DI
services.AddFileStorage<AzureBlobFileStorage>();
```

Each provider package ships an `Add{Provider}Storage(options)` helper that resolves the SDK client from the container and registers the provider as the `IFileStorage` singleton; `AddAzureBlobStorage` also has a `(options, connectionString)` overload that registers the `BlobServiceClient` for you. `AddFileStorage<T>()` activates the type from the container, so its constructor dependencies must be registered too -- for `AzureBlobFileStorage`, that means a `BlobServiceClient` and an `AzureBlobStorageOptions` singleton.

### PragmaticBuilderStorageExtensions

Registration via `IPragmaticBuilder` (recommended for Pragmatic.Composition hosts):

```csharp
// Factory-based registration (access to IServiceProvider for resolving dependencies)
app.UseStorage(sp => new LocalDiskFileStorage(
    basePath,
    sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));

// Type-based registration (constructor dependencies must be registered separately)
app.UseStorage<S3FileStorage>();
```

Both `UseStorage` overloads return `IPragmaticBuilder` for fluent chaining. For the cloud providers, factory registration is the simplest path: it constructs the SDK client and options inline, without registering them as separate services.

### Singleton Lifetime

All registration methods register `IFileStorage` as a **singleton**. This is appropriate because:
- `LocalDiskFileStorage` has no per-request state (it only holds a `basePath`, an `ILogger`, and the size limit).
- Cloud providers hold a client instance (`BlobServiceClient`, `IAmazonS3`) that is designed for reuse across requests.
- File storage operations are stateless: each call is independent.

---

## Streaming

`IFileStorage.SaveAsync` accepts a `Stream`, not a `byte[]`. This enables efficient handling of large files without loading the entire content into memory.

### Typical Upload Flow

```csharp
// IFormFile → Stream → IFileStorage
await using var stream = formFile.OpenReadStream();
var uri = await _storage.SaveAsync(stream, formFile.FileName, "photos", ct);
```

### Important Stream Rules

1. **Do not assume the stream is seekable.** Not all streams support `Position` or `Seek()`. Read the stream forward once.
2. **Dispose the stream after use.** The caller owns the stream lifetime -- both the stream passed to `SaveAsync` and the stream returned by `GetAsync`. Use `await using`.
3. **Do not read the stream before passing it.** If you consume the stream before `SaveAsync`, the provider receives an empty stream. If you need to inspect the content, save to a `MemoryStream` first, reset position, then pass to `SaveAsync`.

### Size Limits

Providers with a size limit (`LocalDiskFileStorage` with `maxFileSizeBytes > 0`, `S3FileStorage` with `MaxFileSizeBytes > 0`) enforce it in two ways: seekable streams are rejected up front by checking `Length`; non-seekable streams (HTTP request bodies, chunked inputs) are rejected mid-copy the moment the running byte total exceeds the limit. Both paths throw `InvalidOperationException`.

---

## Ecosystem Integration

### Actions

When a domain action declares a private `IFileStorage` field with `= null!`, the Actions SG generates a `SetDependencies()` method that injects the storage service from the DI container:

```csharp
public class UploadPhoto : DomainAction<Uri>
{
    private IFileStorage _storage = null!;  // Injected automatically

    public required IFormFile File { get; init; }

    public override async Task<Result<Uri, IError>> Execute(CancellationToken ct)
    {
        await using var stream = File.OpenReadStream();
        return await _storage.SaveAsync(stream, File.FileName, "photos", ct);
    }
}
```

### Endpoints

File upload endpoints use `IFileStorage` in combination with `[FromForm]` for `IFormFile` binding:

```csharp
[Endpoint(HttpVerb.Post, "/api/products/{productId}/photo")]
public partial class UploadProductPhoto : Endpoint<Uri>
{
    private IFileStorage _storage = null!;

    [FromRoute]
    public required Guid ProductId { get; init; }

    [FromForm]
    [MaxFileSize(5 * 1024 * 1024)]
    [AllowedContentTypes("image/jpeg", "image/png")]
    public required IFormFile Photo { get; init; }

    public override async Task<Result<Uri>> HandleAsync(CancellationToken ct)
    {
        await using var stream = Photo.OpenReadStream();
        return await _storage.SaveAsync(stream, Photo.FileName, "product-photos", ct);
    }
}
```

### Messaging

Pragmatic.Messaging's claim-check pattern uses `IFileStorage` to keep large payloads out of the message broker: the payload is stored with `SaveAsync`, the message carries the URI, and the consumer reads the payload back with `GetAsync`.

### IPragmaticBuilder

Storage follows the Pragmatic builder pattern for module strategy configuration (Tier 2). The provider is chosen once in `Program.cs`, and the same `IFileStorage` is injected everywhere:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    if (app.Environment.IsDevelopment())
        app.UseStorage(sp => new LocalDiskFileStorage(
            Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
            sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
    else
        app.UseStorage(sp => new S3FileStorage(
            new AmazonS3Client(),
            new S3StorageOptions { BucketName = "my-app-uploads" },
            sp.GetRequiredService<ILogger<S3FileStorage>>()));
});
```

### Testing

Reference the `Pragmatic.Storage.InMemory` package and register `InMemoryFileStorage`; it backs all four `IFileStorage` methods (and `IFileInfoProvider`) with a dictionary, no filesystem or network:

```csharp
using Pragmatic.Storage.InMemory;

services.AddInMemoryStorage();
```

Every `SaveAsync` returns a resolvable URI, `GetAsync`/`ExistsAsync` round-trip it, and `DeleteAsync` is idempotent, so an upload endpoint under test behaves as it would in production without touching disk or a cloud account.

---

## See Also

- [Getting Started](getting-started.md) -- Step-by-step file upload and download
- [Custom Providers](custom-providers.md) -- The shipped Azure/S3 providers and how to implement IFileStorage for other backends
