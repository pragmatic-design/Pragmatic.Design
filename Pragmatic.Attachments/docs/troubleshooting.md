# Troubleshooting

## Nothing is generated

Check:

- the parent entity is marked `partial`
- `[Entity]` is present
- the project references the Pragmatic source generator path used elsewhere in the repo

## Uploads are rejected

Inspect:

- `MaxPerEntity`
- `MaxFileSizeBytes`
- `AllowedExtensions`

## Endpoints are missing

If entity artifacts appear but HTTP endpoints do not, confirm that the parent entity has `[Resource("segment")]` and that the consuming project references the endpoint module used by the generator pipeline.

## Metadata exists but `/content` returns 404

The metadata row is there but `IFileStorage.GetAsync` returned nothing: the blob was removed out of band, a bucket lifecycle rule expired it, the database was restored from a snapshot newer than the storage, or the storage provider is pointed at a different container than the one the upload wrote to. The download action deliberately answers 404 rather than 500 — nothing is broken server-side and no retry helps.

Check the row's `StorageUri` in the database (it is not exposed over HTTP) against the configured provider and container.

## The purge job is not running

`PurgeDeletedAfterDays` must be positive — `0` (the default) generates no job at all. The consuming project must also reference `Pragmatic.Jobs`, otherwise the job is skipped; and the job only picks rows whose `DeletedAt` is older than the window, so a freshly deleted attachment is expected to survive.

