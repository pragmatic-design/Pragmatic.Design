# Pragmatic.Storage Samples

Runnable samples for `Pragmatic.Storage` and its provider packages.

## Running

From this directory: `dotnet run`.

A single executable runs every sample in sequence. Local-disk samples use a unique
temporary directory that is cleaned up afterwards. Cloud-provider samples are setup-only
(no network) and require live credentials to perform real operations.

## Samples

| Sample | Demonstrates |
|--------|--------------|
| `SaveAndDeleteSample` | `SaveAsync` / `DeleteAsync` lifecycle on local disk |
| `GetAndExistsSample` | `GetAsync` (stream read) and `ExistsAsync` (presence probe) |
| `MaxFileSizeSample` | `maxFileSizeBytes` enforcement on `LocalDiskFileStorage` |
| `PathTraversalSample` | Path-traversal / absolute-path rejection |
| `MimeTypesSample` | `MimeTypes.GetMimeType` extension mapping + fallback |
| `DependencyInjectionSample` | `AddFileStorage<T>()` and `AddLocalDiskStorage()` DI registration |
| `PragmaticBuilderStorageSample` | `IPragmaticBuilder.UseStorage<T>()` and `UseStorage(factory)` |
| `AzureBlobStorageSample` | `AzureBlobStorageOptions` + `AzureBlobFileStorage` construction (setup-only) |
| `S3StorageSample` | `S3StorageOptions` + `S3FileStorage` construction (setup-only) |

`AddFileStorage<T>()` / `UseStorage<T>()` require a DI-constructible storage type, so the samples
use a small in-memory implementation (`InMemoryFileStorage`) to demonstrate the generic overloads;
`LocalDiskFileStorage` (which needs a base path) is shown via the factory/helper overloads instead.

## Note on the relative-URI round-trip

`SaveAsync(content, fileName, container)` returns the `Uri` of the stored file
(e.g. `/files/uploads/{guid}.txt`). `GetAsync` / `ExistsAsync` / `DeleteAsync` all take
**that same `Uri`** — pass back what `SaveAsync` returned rather than reconstructing a path.
