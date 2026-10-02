---
name: pragmatic-use-storage
description: Use when the app stores or serves files (uploads, avatars, generated PDFs, imports, exports) — Pragmatic.Storage, one IFileStorage over local disk, Azure Blob or S3/R2, chosen in the host.
---

# Pragmatic Use Storage

**Covers:** Provider-agnostic file storage with Pragmatic.Storage from NuGet — IFileStorage (Save/Get/Exists/Delete), LocalDisk for dev, Azure Blob and S3/R2 in production, chosen once in the host.

`Pragmatic.Storage` gives domain code one `IFileStorage` interface; the physical backend is chosen once
in `Program.cs`. Domain code never references a cloud SDK, so it runs and tests the same everywhere.

## When to use

- Persisting or retrieving binary content (documents, images, CSV imports).
- You want to develop on local disk and switch to Azure Blob / S3 in production with one line.

## Packages

```xml
<PackageReference Include="Pragmatic.Storage" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.Storage.Azure" Version="1.0.0-alpha.*" />   <!-- or .S3 / .GoogleCloud / .Sftp / .Ftp / .InMemory -->
```

Providers: **LocalDisk** (built-in, dev), **Azure** Blob, **S3** (AWS / Cloudflare R2 / MinIO / Wasabi / DO / B2), **GoogleCloud**, **Sftp**, **Ftp/FTPS**, and **InMemory** (`Pragmatic.Storage.InMemory`, for tests / local dev — `AddInMemoryStorage()`). Domain code is identical across all of them.

## Core pattern

Inject `IFileStorage` (in actions, dependencies are uninitialized private fields):

```csharp
[DomainAction]
public partial class UploadPhotoAction : DomainAction<Uri>
{
    private IFileStorage _storage = null!;
    public required IFormFile File { get; init; }

    public override async Task<Result<Uri, IError>> Execute(CancellationToken ct = default)
    {
        await using var stream = File.OpenReadStream();
        return await _storage.SaveAsync(stream, File.FileName, container: "photos", ct);
    }
}
```

`SaveAsync(Stream content, string fileName, string container, CancellationToken ct)` — the provider generates a GUID-based stored name (the original `fileName` only drives the extension/content-type) and returns the `Uri` to persist. The other methods take that `Uri` back: `GetAsync(uri)` → `Stream?` (null if missing, caller disposes), `ExistsAsync(uri)` → `bool`, `DeleteAsync(uri)` (idempotent no-op if missing).

⚠️ **The URI shape is provider-defined, and a URI from another provider is a caller error.** LocalDisk
returns a *relative* path so the file is servable as a static asset; the memory store returns
`mem://…`; S3 returns `s3://…`. Hand one provider a URI another wrote and **every one of the seven
throws `ArgumentException`** — it is not a missing file.

Two questions, two answers, and keeping them apart is the point:

| | |
|---|---|
| "I do not have that file" | `null` / `false` / a delete that does nothing |
| "that is not one of my addresses" | `ArgumentException`, at the call site |

So a caller asking about a file that may have been deleted needs no `try`. A caller that gets an
`ArgumentException` has a **wiring** mistake — a stale row written when the app ran on a different
provider — and it says so loudly instead of reading as an ordinary absence.

⚠️ Writing your own provider: resolve the URI **once**, in a private method the four read methods
share. A provider that refused a foreign URI from one method and answered from another would be the
same divergence one level down.

## Wiring the backend (host)

Inside the `PragmaticApp.RunAsync(args, app => …)` callback — `UseStorage` is on `IPragmaticBuilder`:

```csharp
app.UseStorage(sp =>
{
    var basePath = Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "wwwroot");
    return new LocalDiskFileStorage(basePath, sp.GetRequiredService<ILogger<LocalDiskFileStorage>>());
});
```

Production providers (domain code unchanged): register through `UseStorage`, or register the SDK client and call the provider's `Add{Provider}Storage(options)` DI helper (`AddAzureBlobStorage`, `AddS3Storage`, `AddGoogleCloudStorage`, `AddSftpStorage`, `AddFtpStorage`).

```csharp
// Azure Blob (Pragmatic.Storage.Azure)
app.UseStorage(sp => new AzureBlobFileStorage(
    new BlobServiceClient(config["Storage:AzureConnectionString"]),
    new AzureBlobStorageOptions { ContainerPrefix = "myapp-" },
    sp.GetRequiredService<ILogger<AzureBlobFileStorage>>()));

// S3 / R2 (Pragmatic.Storage.S3) — via DI helper
app.Services.AddSingleton<IAmazonS3>(new AmazonS3Client());
app.Services.AddS3Storage(new S3StorageOptions { BucketName = "myapp-files", MaxFileSizeBytes = 10 * 1024 * 1024 });
```

### Public or private — decide before the first upload

Where the bytes live decides who can read them, and no permission check stands in between:

| Setup | Who can fetch a stored file |
|---|---|
| LocalDisk under `wwwroot` + `app.UseStaticFiles()` | **anyone with the URI** — it is a static asset; the GUID name makes it unguessable, not private |
| S3 / Google with `PublicBaseUrl` | anyone with the URI, through the CDN or website endpoint |
| Azure container with public access | anyone with the URI |
| LocalDisk **outside** `wwwroot`; S3/Google without `PublicBaseUrl`; private Azure container | nobody directly — the application streams it with `GetAsync` from an endpoint that checks permissions, or hands out a short-lived signed URL (below) |

⚠️ Invoices, contracts, identity documents, anything personal: private. `[HasAttachments]`
(`pragmatic-use-traits`) already serves its files through a permission-checked `/content` route — put
its storage somewhere private, or the permission guards a door next to an open window.

Container names must be flat (`"photos"`, `"invoices"`) — no `/` separators: LocalDisk / S3 / GoogleCloud tolerate nested paths but Azure container names do not, so keep them portable.

## Result contract (Mutation path)

Each method has a `*AsResultAsync` counterpart returning `Result<T, IError>` (`VoidResult<IError>` for delete) with typed errors — `FileTooLargeError` (413), `StorageFileNotFoundError` (404), transient-aware `StorageWriteError` (500). Prefer it in actions/mutations: no `try`/`catch`, composes with the pipeline.

```csharp
[DomainAction]
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

Use the throwing `SaveAsync`/`GetAsync`/… surface for direct, out-of-pipeline use (oversize → `FileSizeLimitExceededException`, a subtype of `InvalidOperationException`).

## Signed URLs & metadata (optional capabilities)

`ISignedUrlProvider` (Azure SAS / S3 pre-signed / Google signed URL — not LocalDisk/InMemory/SFTP/FTP) mints a temporary URL so the browser downloads a private file directly. `IFileInfoProvider.GetInfoAsync(uri)` returns size/content-type/last-modified without downloading. Pattern-match to reach them:

```csharp
if (storage is ISignedUrlProvider signer)
{
    var url = await signer.GetDownloadUrlAsync(fileUri, TimeSpan.FromMinutes(15), ct);
    return Results.Redirect(url.ToString());
}
// else: stream through storage.GetAsync(fileUri, ct)
```

Azure SAS needs a `BlobServiceClient` built with a shared-key credential (else `NotSupportedException`); Google needs a service-account credential (a `UrlSigner` or `GOOGLE_APPLICATION_CREDENTIALS`).

Store a reference (the returned key/Uri) on your entity; never the bytes. See
`Pragmatic.Storage/docs/` for the entity file-reference pattern and custom providers.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Casework and Invoicing example
applications — code that compiles and that `Casework.IntegrationTests` and `Invoicing.IntegrationTests`
exercise — and kept identical to it by the gate: an upload with its limits declared, the download that
answers 404 for another tenant's file, the host's `UseStorage`, and the in-memory store in tests.

Signed URLs are used by no tested application yet, so there is no example of them here: the sections
above are the reference.
