# Troubleshooting

Practical problem/solution guide for Pragmatic.Storage. Each section covers a common issue, the likely causes, and the fix.

---

## File Saves But Returns Empty Content

The `SaveAsync` call succeeds and returns a valid URI, but the stored file has zero bytes.

### Checklist

1. **Was the stream read before passing to SaveAsync?** If you inspect or copy the stream before saving, the position is at the end. Reset with `ms.Position = 0` (only works with seekable streams like `MemoryStream`):

   ```csharp
   ms.Position = 0;  // Reset before saving
   var uri = await _storage.SaveAsync(ms, fileName, container, ct);
   ```

2. **Is the IFormFile empty?** Check `File.Length > 0` before saving. A zero-length file produces a valid but empty storage entry.

3. **Was the stream disposed before SaveAsync completed?** If the stream's scope ends before the async save finishes, the content is lost. Use `await` on the save call within the stream's `using` scope.

---

## 404 When Accessing Stored Files (LocalDisk)

Files are saved to disk but HTTP requests to the URI return 404.

### Checklist

1. **Is `UseStaticFiles()` in the middleware pipeline?**

   ```csharp
   app.UseStaticFiles();  // Must be called to serve files from wwwroot/
   ```

2. **Does the base path match the static files root?** `LocalDiskFileStorage` saves to `{basePath}/files/`. If `basePath` is `wwwroot`, static files serve from `wwwroot/`. These must match.

3. **Is the request URL correct?** `LocalDiskFileStorage` returns relative URIs like `/files/photos/abc.jpg`. Verify the client is requesting the exact URI (no extra path segments, correct casing on case-sensitive filesystems).

4. **Is the file actually on disk?** Navigate to `{basePath}/files/{container}/` and verify the file exists. If the file is missing, the save may have failed silently (check logs).

---

## InvalidOperationException: Unable to Resolve IFileStorage

At runtime, resolving `IFileStorage` throws because no implementation is registered.

### Checklist

1. **Did you call a registration method?** One of these must be called before the host builds:

   ```csharp
   // Option 1: IPragmaticBuilder
   app.UseStorage(sp => new LocalDiskFileStorage(basePath, sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));

   // Option 2: Direct DI
   builder.Services.AddLocalDiskStorage(basePath);

   // Option 3: Custom provider
   builder.Services.AddFileStorage<MyProvider>();
   ```

2. **Is the registration conditional and the condition not met?** If you use `if (env.IsDevelopment())` to register local disk but the environment is not Development, no provider is registered. Ensure all code paths register a provider.

3. **Is the registration happening after `builder.Build()`?** Service registrations must happen before `Build()`. `UseStorage` on `IPragmaticBuilder` runs during the builder phase, so this typically works. But `AddLocalDiskStorage` on `IServiceCollection` must be called before `Build()`.

---

## InvalidOperationException: "Upload rejected: ... exceeds the limit"

`SaveAsync` throws `InvalidOperationException` with an "Upload rejected" message.

### Cause

A size limit is configured and the upload exceeds it:

- **LocalDisk**: the `maxFileSizeBytes` constructor parameter (`0` = no limit, the default).
- **S3**: `S3StorageOptions.MaxFileSizeBytes` (`0` = no limit, the default).

Enforcement happens in two ways. Seekable streams are rejected up front by checking `Length`, before anything is written. Non-seekable streams (HTTP request bodies, chunked inputs) have no known length, so the limit is enforced mid-transfer: the copy aborts the moment the running byte total exceeds the limit. On LocalDisk the partial file is removed before the exception propagates.

### Fix

1. Raise the limit (or set it to `0`) if the file size is legitimate.
2. Reject oversized uploads earlier at the endpoint with `[MaxFileSize(...)]` so clients get a proper 4xx instead of a server-side exception.
3. Note that `AddLocalDiskStorage(basePath)` registers LocalDisk **without** a size limit. To set one, use factory registration:

   ```csharp
   app.UseStorage(sp => new LocalDiskFileStorage(
       basePath,
       sp.GetRequiredService<ILogger<LocalDiskFileStorage>>(),
       maxFileSizeBytes: 10 * 1024 * 1024));
   ```

---

## GetAsync Returns Null / ExistsAsync Returns False for a File That Exists (LocalDisk)

`GetAsync` returns `null` (or `ExistsAsync` returns `false`, or `DeleteAsync` silently does nothing) even though the file is on disk.

### Cause

`LocalDiskFileStorage` resolves every incoming URI safely against its `basePath`. URIs that are rooted (`C:\...`, UNC paths), contain traversal segments (`../`), or otherwise resolve outside the storage root are **treated exactly like a missing file**: `null` / `false` / no-op. A rejected path is deliberately indistinguishable from a not-found, so callers probing with crafted URIs learn nothing about the filesystem.

### Checklist

1. **Pass back the URI returned by `SaveAsync`**, unmodified. Relative URIs look like `/files/photos/abc.jpg`.
2. **Same `basePath`?** If the provider was constructed with a different `basePath` than the one used when saving (e.g., changed between deployments, or different between two hosts), the resolved path points elsewhere.
3. **No manual path building.** Do not reconstruct URIs from filesystem paths -- absolute paths are rejected by design.

---

## ArgumentException When Reading or Deleting (Azure/S3)

`GetAsync`, `ExistsAsync`, or `DeleteAsync` throws `ArgumentException` on the cloud providers.

### Cause

Both cloud providers validate caller-supplied URIs so they cannot be used to address arbitrary storage:

- **Azure** -- "The supplied URI host ... does not match the configured storage account": the URI's host differs from the `BlobServiceClient`'s account. Happens when URIs saved against one account (or against Azurite) are replayed against another.
- **S3** -- "Cannot resolve ... to an S3 key": the URI is neither an `s3://` URI nor prefixed with the configured `PublicBaseUrl`. Happens when `PublicBaseUrl` changed after URIs were issued, or when the URI was hand-built.

### Fix

Store and pass back the exact `Uri` returned by `SaveAsync`. If you change the storage account or `PublicBaseUrl`, previously issued URIs no longer resolve -- migrate the stored references along with the configuration.

---

## NotSupportedException When Generating a Signed URL

`ISignedUrlProvider.GetDownloadUrlAsync` (or `GetDownloadUrlAsResultAsync`) fails because the provider cannot sign.

### Cause

Signing needs a credential the client was not built with:

- **Azure** -- a SAS URL requires the `BlobServiceClient` to be created with a shared-key credential (a connection string or an account key). A client built from a managed identity or a token credential reports `CanGenerateSasUri == false` and the call throws `NotSupportedException`.
- **Google Cloud** -- signing requires a service-account credential (a private key). The plain `StorageClient` does not carry one; supply a `UrlSigner` in `GoogleCloudStorageOptions` or set `GOOGLE_APPLICATION_CREDENTIALS`.
- **LocalDisk / InMemory / SFTP / FTP** -- these do not implement `ISignedUrlProvider` at all; a `storage is ISignedUrlProvider` check returns `false`. Serve their files another way (static files, or stream through `GetAsync`).

### Fix

Build the cloud client with a signing-capable credential, or fall back to streaming through `GetAsync` when `storage is ISignedUrlProvider` is `false`. The Result-based `GetDownloadUrlAsResultAsync` turns the `NotSupportedException` into a `StorageWriteError` whose `Reason` explains why signing was unavailable.

---

## UriFormatException When Deleting Files

Calling `DeleteAsync` with a URI reconstructed from a string throws `UriFormatException`.

### Cause

`LocalDiskFileStorage.SaveAsync` returns a relative URI (`new Uri("/files/photos/abc.jpg", UriKind.Relative)`). If you store this as a string and reconstruct it with `new Uri(string)`, the default constructor requires an absolute URI.

### Fix

Store the `Uri` directly on your entity, not as a string:

```csharp
// Entity
public Uri FileUri { get; set; } = null!;

// Save
entity.FileUri = await _storage.SaveAsync(stream, fileName, container, ct);

// Delete -- same Uri, no conversion
await _storage.DeleteAsync(entity.FileUri, ct);
```

If you must store as string (e.g., database column), reconstruct with `UriKind`:

```csharp
var uri = new Uri(entity.FileUrl, UriKind.RelativeOrAbsolute);
await _storage.DeleteAsync(uri, ct);
```

---

## Cloud Provider Throws at First Upload

`SaveAsync` throws an exception from the cloud SDK (Azure, S3) on the first call.

### Checklist

1. **Are the provider's constructor dependencies available?** `UseStorage<T>()` and `AddFileStorage<T>()` activate the type from the DI container, so the SDK client **and** the options must be registered too:

   ```csharp
   // Azure
   builder.Services.AddSingleton(new BlobServiceClient(connectionString));
   builder.Services.AddSingleton(new AzureBlobStorageOptions { ContainerPrefix = "myapp-" });
   app.UseStorage<AzureBlobFileStorage>();

   // S3
   builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client());
   builder.Services.AddSingleton(new S3StorageOptions { BucketName = "my-app-uploads" });
   app.UseStorage<S3FileStorage>();
   ```

   Factory registration (`app.UseStorage(sp => new S3FileStorage(...))`) sidesteps this entirely -- everything is constructed inline.

2. **Are the credentials correct?** Check the connection string, access key, or managed identity configuration.

3. **Does the bucket exist? (S3)** `S3FileStorage` does not create the bucket -- create it before the first upload. Azure is different: `AzureBlobFileStorage` creates blob containers automatically on first save.

4. **Is the container name valid? (Azure)** Azure blob container names must be lowercase letters, digits, and hyphens -- no slashes, 3-63 characters. `ContainerPrefix + container` must satisfy these rules. Keep container names flat (`"product-photos"`, never `"products/photos"`).

5. **Is the network accessible?** Cloud storage requires network access. In restricted environments (corporate firewalls, air-gapped), HTTP calls to the storage service may be blocked.

---

## File Deleted But Entity Still References It

Users see broken images or download links that return 404.

### Cause

The file was deleted from storage but the entity's `FileUri` property was not cleared or the entity was not removed from the database. There is no transactional guarantee between `IFileStorage` and your persistence layer.

### Fix

Always update the entity when deleting a file:

```csharp
// Option 1: Delete the entity entirely
await _storage.DeleteAsync(document.FileUri, ct);
await _repository.RemoveAsync(document, ct);
await _repository.SaveAsync(ct);

// Option 2: Clear the URI
await _storage.DeleteAsync(document.FileUri, ct);
document.SetFileUri(null);
await _repository.SaveAsync(ct);
```

For maximum safety, delete the entity reference first (preventing further access), then delete the physical file.

---

## Large File Uploads Fail with OutOfMemoryException

Uploading files larger than ~100 MB causes `OutOfMemoryException`.

### Cause

If you read the entire file into a `byte[]` or `MemoryStream` before passing to `SaveAsync`, the full content is held in memory. For large files, this exhausts available memory.

### Fix

Pass the `IFormFile` stream directly to `SaveAsync` without buffering:

```csharp
await using var stream = File.OpenReadStream();
var uri = await _storage.SaveAsync(stream, File.FileName, container, ct);
```

If you need to buffer (e.g., for content inspection), consider streaming to a temporary file first:

```csharp
var tempPath = Path.GetTempFileName();
try
{
    await using (var tempFs = System.IO.File.Create(tempPath))
        await File.CopyToAsync(tempFs, ct);

    // Inspect the temp file
    // ...

    await using var readStream = System.IO.File.OpenRead(tempPath);
    var uri = await _storage.SaveAsync(readStream, File.FileName, container, ct);
}
finally
{
    System.IO.File.Delete(tempPath);
}
```

Also check Kestrel's request body size limit:

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024;  // 200 MB
});
```

---

## FAQ

### Can I use multiple storage providers simultaneously?

Not with the default registration. `IFileStorage` is a single singleton. If you need different providers for different containers (e.g., local disk for temp files, S3 for permanent storage), implement a routing `IFileStorage` that delegates based on the container name.

### Does IFileStorage support reading files?

Yes. `GetAsync(fileUri, ct)` returns a `Stream?` -- `null` if the file does not exist. The caller owns the stream; dispose it with `await using` (on the cloud providers, disposal also releases the underlying HTTP connection). Use it to stream private files to authorized users, post-process uploads, or read back payloads server-side -- Pragmatic.Messaging's claim-check pattern is built on it. Public files are still best served by the URI directly (static files, CDN, presigned URL).

### Why is my file served as application/octet-stream?

The cloud providers set the content type from the file's extension via `MimeTypes.GetMimeType`. Extensions outside the known set (common image, document, data, and media formats) fall back to `application/octet-stream`. There is no content sniffing -- the type derives entirely from the caller-supplied `fileName`, so treat it as untrusted metadata and validate uploads at the endpoint with `[AllowedContentTypes]`.

### Can I use LocalDiskFileStorage in Docker?

Yes, but mount a volume for persistence. Without a volume, files are stored in the container's ephemeral filesystem and lost on restart:

```yaml
volumes:
  - ./data/files:/app/wwwroot/files
```

### How do I test file upload endpoints?

Reference the `Pragmatic.Storage.InMemory` package and call `services.AddInMemoryStorage()` in your test setup. `InMemoryFileStorage` backs all four methods -- `SaveAsync`, `GetAsync`, `ExistsAsync`, `DeleteAsync` -- (and `IFileInfoProvider`) against a dictionary, without touching the filesystem or a cloud account, so an upload endpoint behaves as it would in production.

### What happens if DeleteAsync is called with a non-existent URI?

Nothing -- delete is idempotent on every provider. `LocalDiskFileStorage` checks `File.Exists()` and silently skips; `AzureBlobFileStorage` uses `DeleteIfExistsAsync`; `S3FileStorage` swallows 404/`NoSuchKey` responses. Custom providers should follow the same pattern.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working file upload/download endpoints.
- **Providers**: See [custom-providers.md](custom-providers.md) for the shipped Azure Blob and S3 providers and the contract for writing your own.
