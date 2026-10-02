---
title: "Pragmatic.Storage"
description: "Provider-agnostic file storage for the Pragmatic.Design ecosystem."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Storage/README.md
sidebar:
  order: 0
  label: Overview
---
Provider-agnostic file storage for the Pragmatic.Design ecosystem.

## The Problem

Every non-trivial app stores files (avatars, PDFs, CSV imports) and the backend changes by
environment: local disk in dev, Azure Blob in staging, S3 in production. Without an abstraction, domain
code couples to a cloud SDK: switching providers means rewriting business logic, running locally needs
an emulator, and unit tests must mock the whole SDK surface.

```csharp
// Without Pragmatic.Storage: coupled to the Azure Blob SDK, so it cannot run or be tested without Azure
var blob = _container.GetBlobClient($"{Guid.NewGuid()}{ext}");
await blob.UploadAsync(file.OpenReadStream(), cancellationToken: ct);
```

## The Solution

A minimal `IFileStorage` interface: `SaveAsync`, `GetAsync`, `ExistsAsync`, `DeleteAsync`. Domain code
depends on the abstraction; the physical backend is chosen once in `Program.cs`. Ships with
`LocalDiskFileStorage` for development; swap to Azure or S3 with one line, zero changes to domain code.

```csharp
public partial class UploadPhotoAction : DomainAction<Uri>
{
    private IFileStorage _storage = null!;          // injected
    public required IFormFile File { get; init; }

    public override async Task<Result<Uri, IError>> Execute(CancellationToken ct)
    {
        await using var stream = File.OpenReadStream();
        Uri uri = await _storage.SaveAsync(stream, File.FileName, container: "photos", ct);
        return uri;
    }
}
```

## Features

- **`IFileStorage`**: four methods: `SaveAsync` (returns the file `Uri`), `GetAsync` (read stream,
  `null` if missing), `ExistsAsync`, `DeleteAsync` (idempotent: no-op if already gone).
- **Result-based contract**: `SaveAsResultAsync` / `GetAsResultAsync` / `ExistsAsResultAsync` /
  `DeleteAsResultAsync` return `Result<T, IError>` (`VoidResult<IError>` for delete) with typed errors
  such as `FileTooLargeError` (413), `StorageFileNotFoundError` (404), `StorageWriteError` (500). Composes
  into an action/mutation with no `try`/`catch`. The throwing surface stays for direct use.
- **File metadata**: `IFileInfoProvider.GetInfoAsync(uri)` returns a `StoredFileInfo` (size, content
  type, last-modified) without downloading the file. Implemented by every provider.
- **Signed download URLs**: `ISignedUrlProvider.GetDownloadUrlAsync(uri, expiry)` mints a temporary
  pre-authenticated URL so a private file is served straight from the backend, no proxying. Azure
  (SAS), S3/R2 (pre-signed), and Google Cloud (signed URL) implement it.
- **`LocalDiskFileStorage`**: saves under `{basePath}/files/{container}/` with GUID file names and
  relative URIs; optional `maxFileSizeBytes` upload cap (enforced for seekable *and* non-seekable
  streams); atomic writes (temp file + rename); path-traversal protection on both write (container)
  and read/delete (URI).
- **Cloud & network providers**: Azure Blob (`AzureBlobFileStorage`), S3 / Cloudflare R2 / MinIO /
  Wasabi / Backblaze B2 (`S3FileStorage`), Google Cloud Storage (`GoogleCloudFileStorage`), SFTP
  (`SftpFileStorage`), and FTP / FTPS (`FtpFileStorage`), each with size limits, URI/key validation,
  and idempotent delete.
- **`InMemoryFileStorage`**: the `Pragmatic.Storage.InMemory` package for tests and local
  development; `AddInMemoryStorage()`, no filesystem or network.
- **`MimeTypes`**: content-type from file extension (common web/office types, fallback
  `application/octet-stream`). Extension-based only; no content sniffing.
- **`LimitedReadStream`**: read-only wrapper that enforces a byte cap on non-seekable streams
  (used by the S3 provider; reusable in custom providers).
- **Registration**: `AddLocalDiskStorage(basePath[, maxFileSizeBytes])`, `AddInMemoryStorage()`,
  `AddAzureBlobStorage`, `AddS3Storage`, `AddGoogleCloudStorage`, `AddSftpStorage`, `AddFtpStorage`,
  and `AddFileStorage<T>()` on `IServiceCollection`; `UseStorage(factory)` / `UseStorage<T>()` on
  `IPragmaticBuilder`.
- **Observability**: providers log via `[LoggerMessage]` (zero-allocation structured logging).

## Installation

```bash
dotnet add package Pragmatic.Storage
dotnet add package Pragmatic.Storage.Azure   # or .S3 / .GoogleCloud / .Sftp / .Ftp / .InMemory
```

| Package | Purpose |
|---------|---------|
| `Pragmatic.Storage` | The `IFileStorage` contract, `LocalDiskFileStorage`, Result-based surface and typed errors, `IFileInfoProvider` / `ISignedUrlProvider`, DI/builder extensions, `MimeTypes`, `LimitedReadStream` |
| `Pragmatic.Storage.Azure` | Azure Blob Storage provider (`AzureBlobFileStorage` + `AzureBlobStorageOptions`); metadata + SAS signed URLs |
| `Pragmatic.Storage.S3` | Amazon S3 / Cloudflare R2 / MinIO / Wasabi / Backblaze B2 provider (`S3FileStorage` + `S3StorageOptions`); metadata + pre-signed URLs |
| `Pragmatic.Storage.GoogleCloud` | Google Cloud Storage provider (`GoogleCloudFileStorage` + `GoogleCloudStorageOptions`); metadata + signed URLs |
| `Pragmatic.Storage.Sftp` | SFTP provider (`SftpFileStorage` + `SftpStorageOptions`); metadata; no signed URLs |
| `Pragmatic.Storage.Ftp` | FTP / FTPS provider (`FtpFileStorage` + `FtpStorageOptions`); metadata; no signed URLs |
| `Pragmatic.Storage.InMemory` | `InMemoryFileStorage` for tests and local development (`AddInMemoryStorage()`) |

Register the backend in `Program.cs` (`app.UseStorage(...)`): LocalDisk for dev, Azure/S3 in
production. Domain code is unchanged across environments.

## Status

**Stable** within 1.0.0-alpha: the `IFileStorage` contract, `LocalDiskFileStorage`, and the Azure/S3
providers are settled. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/storage/concepts/) | The abstraction, container organization, entity file-reference pattern |
| [Getting Started](/modules/storage/getting-started/) | Save/get/delete a file, wire a backend, environment switching |
| [Custom Providers](/modules/storage/custom-providers/) | Implement `IFileStorage` for a new backend |
| [Common Mistakes](/modules/storage/common-mistakes/) | The most frequent storage pitfalls |
| [Troubleshooting](/modules/storage/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/storage/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Storage is **MIT-licensed**.
