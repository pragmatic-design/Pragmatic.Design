---
title: "Storage Providers"
description: "The default `LocalDiskFileStorage` writes files to a local directory. Provider packages ship with the framework: `Pragmatic.Storage.Azure` (Azure Blob), `Pragma"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Storage/docs/custom-providers.md
sidebar:
  order: 3
---
The default `LocalDiskFileStorage` writes files to a local directory. Provider packages ship with the framework: `Pragmatic.Storage.Azure` (Azure Blob), `Pragmatic.Storage.S3` (Amazon S3 / Cloudflare R2 / MinIO / Wasabi / DigitalOcean Spaces / Backblaze B2), `Pragmatic.Storage.GoogleCloud` (Google Cloud Storage), `Pragmatic.Storage.Sftp` (SFTP), `Pragmatic.Storage.Ftp` (FTP / FTPS), and `Pragmatic.Storage.InMemory` (tests / local dev). For any other backend, implement `IFileStorage` yourself.

---

## IFileStorage Interface

The storage abstraction is intentionally minimal -- four methods cover the entire lifecycle of a stored file.

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

- **`SaveAsync`** -- stores the content stream and returns a URI that the other three methods can resolve.
- **`GetAsync`** -- opens a read stream to a stored file; returns `null` if the file does not exist. The caller disposes the stream.
- **`ExistsAsync`** -- checks whether a file exists at the URI.
- **`DeleteAsync`** -- removes the file at the URI; deleting a missing file is a no-op.

The `container` parameter groups files logically (e.g., `"photos"`, `"invoices"`, `"avatars"`). Providers map containers to physical locations (directories, blob containers, key prefixes). Keep container names flat -- no `/` -- so they stay valid on every provider (Azure container names do not allow slashes).

---

## Built-in: LocalDiskFileStorage

Writes files to `{basePath}/files/{container}/{guid}{extension}`:

```csharp
app.UseStorage(sp => new LocalDiskFileStorage(
    basePath: Path.Combine(env.ContentRootPath, "wwwroot"),
    logger: sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
    maxFileSizeBytes: 10 * 1024 * 1024));  // 0 (default) = no limit
```

File names are replaced with GUIDs to avoid collisions. The original extension is preserved. With `maxFileSizeBytes > 0`, oversized uploads throw `InvalidOperationException` -- seekable streams up front, non-seekable streams mid-copy (the partial file is removed).

---

## Shipped: Pragmatic.Storage.Azure

`AzureBlobFileStorage` maps each logical container to a blob container named `{ContainerPrefix}{container}`, creates containers on first use, sets the blob content type from the file extension, and returns the absolute blob URI.

```csharp
public sealed class AzureBlobFileStorage(
    BlobServiceClient blobService,
    AzureBlobStorageOptions options,
    ILogger<AzureBlobFileStorage> logger) : IFileStorage
```

`AzureBlobStorageOptions`: `ContainerPrefix` (default empty), prepended to every container name (`"myapp-"` + `"photos"` → `"myapp-photos"`), and `MaxFileSizeBytes` (`0` = no limit). Implements `IFileInfoProvider` (via `GetProperties`) and `ISignedUrlProvider` (a read-only SAS URI).

### Registration

Construct the provider through `UseStorage`, or use the `AddAzureBlobStorage` DI helper:

```csharp
// Via IPragmaticBuilder — factory registration, everything inline
app.UseStorage(sp => new AzureBlobFileStorage(
    new BlobServiceClient(builder.Configuration.GetConnectionString("Storage")),
    new AzureBlobStorageOptions { ContainerPrefix = "myapp-" },
    sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));

// Via DI helper — register the client (or pass a connection string), then the provider
services.AddSingleton(new BlobServiceClient(connectionString));
services.AddAzureBlobStorage(new AzureBlobStorageOptions { ContainerPrefix = "myapp-" });
// One call: services.AddAzureBlobStorage(new AzureBlobStorageOptions(), connectionString);
```

For local development, point the client at Azurite with `new BlobServiceClient("UseDevelopmentStorage=true")`.

### Security

`GetAsync`, `ExistsAsync`, and `DeleteAsync` validate that the URI host matches the configured storage account and throw `ArgumentException` on mismatch. Always pass back the URI returned by `SaveAsync`. A SAS signed URL requires the `BlobServiceClient` to be built with a shared-key credential; a managed-identity client throws `NotSupportedException` when asked to sign.

---

## Shipped: Pragmatic.Storage.S3

`S3FileStorage` stores files as objects with key `{KeyPrefix}{container}/{guid}{extension}` in a single bucket. Works with any S3-compatible endpoint (AWS, MinIO, Cloudflare R2).

```csharp
public sealed class S3FileStorage(
    IAmazonS3 s3,
    S3StorageOptions options,
    ILogger<S3FileStorage> logger) : IFileStorage
```

| Option | Description |
|--------|-------------|
| `BucketName` | **Required.** The target bucket -- it must already exist. |
| `KeyPrefix` | Optional key prefix (e.g., `"uploads/"`). Default: empty. |
| `PublicBaseUrl` | When set, `SaveAsync` returns `{PublicBaseUrl}/{key}` URIs (CDN / website endpoint); when `null`, it returns `s3://bucket/key` URIs. |
| `MaxFileSizeBytes` | Upload size limit in bytes; `0` (default) = no limit. Enforced up front for seekable streams and mid-transfer (via `LimitedReadStream`) for non-seekable ones. |

Implements `IFileInfoProvider` (via `GetObjectMetadata`) and `ISignedUrlProvider` (a pre-signed GET URL).

### Registration

Construct the provider through `UseStorage`, or register an `IAmazonS3` client and use the `AddS3Storage` DI helper:

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

// Or via DI helper
services.AddSingleton<IAmazonS3>(new AmazonS3Client());
services.AddS3Storage(new S3StorageOptions { BucketName = "my-app-uploads" });
```

For MinIO, Cloudflare R2, Wasabi, DigitalOcean Spaces, or Backblaze B2: `new AmazonS3Client(credentials, new AmazonS3Config { ServiceURL = "https://<endpoint>" })`.

### Security

`GetAsync`, `ExistsAsync`, and `DeleteAsync` resolve the object key only from `s3://` URIs or URIs prefixed with the configured `PublicBaseUrl`; any other URI throws `ArgumentException`. Always pass back the URI returned by `SaveAsync`.

---

## Shipped: Pragmatic.Storage.GoogleCloud

`GoogleCloudFileStorage` stores files as objects (`{ObjectPrefix}{container}/{guid}{extension}`) in a single bucket. Implements `IFileStorage`, `IFileInfoProvider`, and `ISignedUrlProvider`.

```csharp
public sealed class GoogleCloudFileStorage(
    StorageClient storageClient,
    GoogleCloudStorageOptions options,
    ILogger<GoogleCloudFileStorage> logger) : IFileStorage, IFileInfoProvider, ISignedUrlProvider
```

| Option | Description |
|--------|-------------|
| `BucketName` | **Required.** The target bucket -- it must already exist. |
| `ObjectPrefix` | Optional object-name prefix. Default: empty. |
| `PublicBaseUrl` | When set, `SaveAsync` returns `{PublicBaseUrl}/{object}` URIs; when `null`, `gs://bucket/object` URIs. |
| `MaxFileSizeBytes` | Upload size limit in bytes; `0` (default) = no limit. |
| `UrlSigner` | Optional `UrlSigner` for signed URLs — signing needs a service-account credential; without one, `GetDownloadUrlAsync` throws `NotSupportedException`. |

The application registers the `StorageClient`; `AddGoogleCloudStorage(options)` resolves it:

```csharp
services.AddSingleton(StorageClient.Create());  // application-default credentials
services.AddGoogleCloudStorage(new GoogleCloudStorageOptions { BucketName = "my-app-uploads" });
```

---

## Shipped: Pragmatic.Storage.Sftp

`SftpFileStorage` (SSH.NET) opens a connection per operation and stores files at `{BasePath}/{container}/{guid}{extension}`. Implements `IFileStorage` and `IFileInfoProvider` -- no signed URLs.

`SftpStorageOptions`: `Host` (**required**), `Port` (default `22`), `Username` (**required**), `Password?` **or** `PrivateKeyPath?` (with optional `PrivateKeyPassphrase?`), `BasePath` (default `"/"`), `MaxFileSizeBytes` (`0` = no limit).

```csharp
services.AddSftpStorage(new SftpStorageOptions
{
    Host = "sftp.example.com",
    Username = "app",
    PrivateKeyPath = "/secrets/id_rsa",
    BasePath = "/uploads",
});
```

---

## Shipped: Pragmatic.Storage.Ftp

`FtpFileStorage` (FluentFTP) supports plain FTP and explicit FTPS. Implements `IFileStorage` and `IFileInfoProvider` -- no signed URLs.

`FtpStorageOptions`: `Host` (**required**), `Port` (default `21`), `Username` (**required**), `Password?`, `UseSsl` (default `false` — set `true` for FTPS), `BasePath` (default `"/"`), `MaxFileSizeBytes` (`0` = no limit).

```csharp
services.AddFtpStorage(new FtpStorageOptions
{
    Host = "ftp.example.com",
    Username = "app",
    Password = "secret",
    UseSsl = true,
    BasePath = "/data",
});
```

---

## Shipped: Pragmatic.Storage.InMemory

`InMemoryFileStorage` keeps files in a dictionary — for tests and local development, with no filesystem or network. Implements `IFileStorage` and `IFileInfoProvider`.

```csharp
services.AddInMemoryStorage();
```

---

## Writing Your Own Provider

For backends without a shipped package, implement all four methods. This in-memory provider is the smallest complete implementation, shown as a template (for a real in-memory backend, reference the `Pragmatic.Storage.InMemory` package instead of copying this):

```csharp
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public async Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var key = $"{container}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        _files[key] = buffer.ToArray();
        return new Uri($"mem://{key}", UriKind.Absolute);
    }

    public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
        => Task.FromResult(_files.TryGetValue(Key(fileUri), out var bytes)
            ? (Stream)new MemoryStream(bytes, writable: false)
            : null);

    public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
        => Task.FromResult(_files.ContainsKey(Key(fileUri)));

    public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
    {
        _files.TryRemove(Key(fileUri), out _);
        return Task.CompletedTask;
    }

    private static string Key(Uri fileUri) => $"{fileUri.Host}{fileUri.AbsolutePath}";
}
```

### Provider Contract

Follow these rules so your provider behaves like the shipped ones:

| Rule | Detail |
|------|--------|
| GUID naming | Never use `fileName` as the storage name -- generate `{Guid.NewGuid():N}{extension}`. The original name is caller input; the extension is kept for content-type detection. |
| Round-trip URIs | `SaveAsync` returns a URI that your own `GetAsync`/`ExistsAsync`/`DeleteAsync` can resolve. |
| Validate incoming URIs | `fileUri` is caller-supplied. Never let it address arbitrary locations: reject URIs outside your storage root (compare hosts, verify prefixes, resolve paths safely). Every shipped provider throws `ArgumentException` for a URI in another provider's shape -- resolve it once in a private method the four read methods share, so they cannot disagree. |
| `null` on missing get | `GetAsync` returns `null` for a missing file; it does not throw. A URI you could not have written is a different question: that is the `ArgumentException` above, not a missing file. |
| Caller disposes the stream | If your backend's SDK ties the content stream to a response object or connection, return a wrapper stream whose `Dispose` also disposes the response -- this is what the Azure and S3 providers do. |
| Idempotent delete | `DeleteAsync` on a missing file is a no-op. |
| Content type | Use `MimeTypes.GetMimeType(Path.GetExtension(fileName))` if your backend stores a content type. Unknown extensions fall back to `application/octet-stream`. |
| Size limits | If you support an upload cap: reject seekable streams up front via `Length`; wrap non-seekable streams in `LimitedReadStream(content, maxBytes)`, which throws `InvalidOperationException` once the running total exceeds the limit while the destination consumes the bytes. `S3FileStorage` uses exactly this pattern. |
| Stateless singleton | Providers are registered as singletons -- keep them thread-safe and free of per-request state. |

### Optional capabilities

Two optional interfaces let a provider do more than the four core methods. Implement them only when the backend supports them cheaply; callers reach them by pattern-matching (`if (storage is IFileInfoProvider p)`).

| Interface | Method | Implement when |
|-----------|--------|----------------|
| `IFileInfoProvider` | `GetInfoAsync(uri)` → `StoredFileInfo?` (`SizeBytes`, `ContentType`, `LastModified`, `FileUri`); return `null` for a missing file | the backend can report metadata without downloading (a `stat` / `HEAD`) |
| `ISignedUrlProvider` | `GetDownloadUrlAsync(uri, expiry)` → `Uri` | the backend can mint a temporary pre-authenticated download URL; throw `NotSupportedException` if a given configuration cannot sign |

---

## Integration with Endpoints

File upload endpoints use `IFileStorage` for persistence:

```csharp
[Endpoint(HttpVerb.Post, "/api/products/{productId}/photo")]
public partial class UploadProductPhoto : Endpoint<Uri>
{
    private IFileStorage _storage = null!;

    public required Guid ProductId { get; init; }

    [MaxFileSize(5 * 1024 * 1024)]
    [AllowedContentTypes("image/jpeg", "image/png")]
    public required IFormFile Photo { get; init; }

    public override async Task<Result<Uri>> HandleAsync(CancellationToken ct)
    {
        await using var stream = Photo.OpenReadStream();
        var uri = await _storage.SaveAsync(stream, Photo.FileName, "product-photos", ct);
        return uri;
    }
}
```

---

## Provider Selection at Composition Time

The storage provider is chosen once at startup via `IPragmaticBuilder`. The same `IFileStorage` is injected everywhere:

```csharp
if (app.Environment.IsDevelopment())
    app.UseStorage(sp => new LocalDiskFileStorage(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
else
    app.UseStorage(sp => new S3FileStorage(
        new AmazonS3Client(),
        new S3StorageOptions { BucketName = "my-app-uploads" },
        sp.GetRequiredService<ILogger<S3FileStorage>>()));
```

This follows the Pragmatic principle: **default always works** (local disk for dev), **swap at composition** (cloud for production).
