# Pragmatic.Attachments.Samples

Runnable console samples for the **runtime** surface of `Pragmatic.Attachments`.

> [!NOTE]
> `Pragmatic.Attachments` is a **trait package**. Its `[HasAttachments]` **source
> generator IS implemented** — in a real Pragmatic host it emits the typed
> `{Parent}Attachment` metadata entity, an EF Core configuration, the
> upload/download/delete actions and the REST endpoints
> (`/api/.../{parentId}/attachments`). A plain console app does **not** run that
> host SG pipeline, so these samples exercise only the hand-written runtime types
> that execute without it. A `[HasAttachments]`-annotated entity is included as a
> declaration (the attribute compiles) to show how a consumer opts in.
>
> The generator emits a real **multipart/form-data upload endpoint** that binds an
> `IFormFile`; the bytes themselves are written through `Pragmatic.Storage.IFileStorage`
> and the row keeps only the metadata, including the `StorageUri` that points at them.

## Run

```bash
dotnet run --project Pragmatic.Attachments/samples/Pragmatic.Attachments.Samples
```

## What is demonstrated (runtime surface)

| Sample | File | Surface |
|--------|------|---------|
| 1 | `HasAttachmentsOptionsSample.cs` | `HasAttachmentsAttribute` options + defaults (`MaxPerEntity`, `MaxFileSizeBytes`, `AllowedExtensions`, `Container`, `SubBoundary`); free-form, PDF-only and unlimited-image variants |
| 2 | `AttachmentEntitySample.cs` | A concrete `AttachmentBase<Guid>` metadata entity; `Id` ⇄ `PersistenceId` forwarding; file-metadata columns; `StorageUri` (resolved via `IFileStorage`); `ISoftDelete` columns |
| 3 | `UploadValidationSample.cs` | The upload validation contract the SG-generated action enforces before storage: `MaxFileSizeBytes`, `MaxPerEntity`, and the documented `AllowedExtensions` parsing rules (trim, case-insensitive, optional leading dot), run against the real `Contract` entity's options |

## Not covered (depends on the host `[HasAttachments]` SG)

- SG-generated `{Parent}Attachment` entity, EF config and parent navigation collection
- SG-generated upload/download/delete actions and the `IFileStorage` wiring
- SG-generated HTTP endpoints, including the multipart upload and the streaming download

These require the host source-generation pipeline and are intentionally out of scope for a runnable console sample.
