# Pragmatic.Attachments

Add file attachments to any Pragmatic entity with a single attribute.

`Pragmatic.Attachments` is a trait package: you decorate an entity with `[HasAttachments]` and the source generator creates the attachment entity, EF configuration, actions, and HTTP surface in the consuming boundary.

## The Problem

Every "entity with files" feature usually requires the same boilerplate:

- child entity for metadata
- foreign key and indexes
- upload, read-metadata, and delete actions
- endpoint mapping
- storage provider integration
- permissions and DTOs

That boilerplate is repetitive and easy to get wrong.

## The Solution

One attribute activates the whole pipeline:

```csharp
using Pragmatic.Attachments;
using Pragmatic.Persistence.Entity;

[Entity]
[Resource("invoices")]
[HasAttachments]
public partial class Invoice : IEntity
{
    public string Number { get; set; } = "";
}
```

The generator materializes:

- a typed `{Parent}Attachment` entity derived from `AttachmentBase<TEntityId>`
- FK and indexes in EF configuration
- upload, read-metadata (`GetById`), and delete actions
- attachment endpoints under the parent resource
- a navigation collection on the parent entity

## Installation

```xml
<PackageReference Include="Pragmatic.Attachments" />
```

Typical consumers also reference:

- `Pragmatic.Persistence`
- `Pragmatic.Storage`
- `Pragmatic.Endpoints` if HTTP endpoints are desired

## Quick Start

### 1. Mark the entity

```csharp
[Entity]
[Resource("invoices")]
[HasAttachments(MaxPerEntity = 10, AllowedExtensions = ".pdf,.png,.jpg")]
public partial class Invoice : IEntity
{
    public string Number { get; set; } = "";
}
```

### 2. Configure a storage provider

```csharp
using Pragmatic.Storage;

await PragmaticApp.RunAsync(args, builder =>
{
    builder.UseStorage(sp => new LocalDiskFileStorage(
        "wwwroot",
        sp.GetRequiredService<ILogger<LocalDiskFileStorage>>()));
});
```

### 3. Use the generated feature

See **Generated Surface** below for the exact entity, actions, endpoints, DTO, and permission strings.

## Attribute Options

| Option | Default | Description |
|--------|---------|-------------|
| `MaxPerEntity` | `20` | Maximum number of attachments per parent entity. `0` means unlimited. |
| `MaxFileSizeBytes` | `10_485_760` | Maximum upload size in bytes. `0` means unlimited. |
| `AllowedExtensions` | `""` | Comma-separated extension allow-list such as `.pdf,.jpg` (trimmed, case-insensitive, leading dot optional). Empty allows every extension **except** those a browser executes in the origin that serves them — `.html`, `.htm`, `.xhtml`, `.shtml`, `.svg`, `.svgz`, `.xml`, `.js`, `.mjs`, `.mhtml`, `.hta`. Naming one here is the explicit opt-in. |
| `ThumbnailMaxWidth` / `ThumbnailMaxHeight` | `0` (no thumbnail) | Set both and the upload derives a thumbnail of an image, once, where the file is stored; see **Thumbnails** below. |
| `Container` | parent type name in lowercase | Storage container or folder name. |
| `SubBoundary` | `{Parent}Attachments` | Override generated sub-boundary name. |
| `PurgeDeletedAfterDays` | `0` (no purge) | Retention window for soft-deleted attachments. Positive values generate `Purge{Parent}AttachmentsJob`, which deletes the blob and then the row. |
| `PurgeCron` | `"0 3 * * *"` | Cron schedule of that job. Ignored unless `PurgeDeletedAfterDays` is positive. |

## Attachment Model

Generated attachment entities derive from `AttachmentBase<TEntityId>`, which provides:

- `ParentEntityId`
- `FileName`
- `FileSize`
- `ContentType`
- `StorageUri`
- `Description`
- `UploadedBy` and `UploadedAt`
- `ThumbnailUri` (nullable — set only when thumbnails are on and the file is an image)
- soft-delete fields (`IsDeleted`, `DeletedAt`, `DeletedBy`)

The file bytes themselves live in `Pragmatic.Storage`; the database entity stores metadata only.

## Generated Surface

For `[HasAttachments]` on `Invoice` (`[Entity]`, `[Resource("invoices")]`), the source generator emits the following into the consuming boundary's namespace. Types use the parent name as prefix, so `{Parent}` = `Invoice`, FK property `{Parent}Id` = `InvoiceId`.

### Entity — `InvoiceAttachment : AttachmentBase<Guid>`

Adds the FK and parent navigation; everything else is on `AttachmentBase<TEntityId>` (`Id`, `ParentEntityId`, `FileName`, `FileSize`, `ContentType`, `StorageUri`, `Description`, `UploadedBy`, `UploadedAt`, soft-delete fields):

```csharp
public Guid InvoiceId { get; set; }
public Invoice? Invoice { get; set; }
```

EF config (`InvoiceAttachmentEntityConfig : IEntityTypeConfiguration<InvoiceAttachment>`): PK `Id` in the `PersistenceId` column, generated client-side by EF (the upload returns the id straight after `Add`), required `FileName`/`ContentType`/`StorageUri`/`UploadedBy` with max lengths, FK to parent with `OnDelete(Cascade)`, index on `InvoiceId`, and a named `HasQueryFilter("SoftDelete", e => !e.IsDeleted)` soft-delete filter. The parent gets an `ICollection<InvoiceAttachment> Attachments` navigation.

### Actions (`[DomainAction]`)

| Action | Base | Input | Output |
|--------|------|-------|--------|
| `UploadInvoiceAttachmentAction` | `DomainAction<Guid>` | `InvoiceId`, `Stream FileContent`, `FileName`, `long FileSize`, `ContentType`, `string? Description` | new attachment `Guid` |
| `GetInvoiceAttachmentAction` | `DomainAction<InvoiceAttachmentDto>` | `InvoiceId`, `Guid AttachmentId` | `InvoiceAttachmentDto` (or `NotFoundError`) |
| `DownloadInvoiceAttachmentAction` | `DomainAction<FileResponse>` | `InvoiceId`, `Guid AttachmentId` | `FileResponse` (content stream + `ContentType` + `FileName`), or `NotFoundError` |
| `DeleteInvoiceAttachmentAction` | `VoidDomainAction` | `InvoiceId`, `Guid AttachmentId` | soft-deletes (sets `IsDeleted`/`DeletedAt`/`DeletedBy`) |

Every per-attachment action loads the row filtered on **both** the parent id and the attachment id, and skips soft-deleted rows. The parent filter is what prevents an id from another parent being read, downloaded or deleted through a parent the caller is allowed to touch.

`DownloadInvoiceAttachmentAction` resolves the row, then reads the blob through `IFileStorage.GetAsync`. If the row is gone (or belongs to a different parent, or is soft-deleted) the result is `NotFoundError` → **404**. If the row exists but the blob does not, the result is also **404**, not 500: a blob removed out of band is an expected state of the resource, not a server fault a retry could fix.

The upload action enforces, in order, before writing to storage: `MaxPerEntity` (count check → `ForbiddenError`), `MaxFileSizeBytes` (→ `ForbiddenError`), and the extension — against `AllowedExtensions` when set, against the browser-executable list otherwise (→ `ForbiddenError`). It then calls `IFileStorage.SaveAsync(FileContent, FileName, Container, ct)` and records metadata with `UploadedBy = ICurrentUser.Id`.

### List query

`ListInvoiceAttachmentsQuery` (`[Query<InvoiceAttachment, InvoiceAttachmentDto>]`) with `InvoiceId` filter plus `Page` (default 1) / `PageSize` (default 20).

### DTO — `InvoiceAttachmentDto`

`Id`, `InvoiceId`, `FileName`, `FileSize`, `ContentType`, `Description`, `UploadedBy`, `UploadedAt`, plus a static EF `Projection` expression. (Soft-delete fields are not projected.)

`StorageUri` is deliberately **not** on the DTO: it names the storage provider and the bucket layout, and with some providers it is a directly reachable URL. The bytes are served by the download endpoint instead, so the URI never leaves the server.

### Endpoints

Generated under `/api/{boundary}/invoices/{invoiceId}/attachments` (OpenAPI tag `Attachments`):

| Verb | Route | Maps to |
|------|-------|---------|
| `POST` | `/attachments` | `UploadInvoiceAttachmentAction` (201, `multipart/form-data`) |
| `GET` | `/attachments` | `ListInvoiceAttachmentsQuery` (paged) |
| `GET` | `/attachments/{attachmentId}` | `GetInvoiceAttachmentAction` (JSON metadata) |
| `GET` | `/attachments/{attachmentId}/content` | `DownloadInvoiceAttachmentAction` (the file: `Content-Type` from the record, `Content-Disposition` from `FileName`) |
| `GET` | `/attachments/{attachmentId}/thumbnail` | `DownloadThumbnailInvoiceAttachmentAction` — only with thumbnails on and `Pragmatic.Imaging` referenced |
| `DELETE` | `/attachments/{attachmentId}` | `DeleteInvoiceAttachmentAction` (204) |

Metadata and content are separate routes on purpose: one URL returns JSON and the other returns bytes, with no content negotiation to get wrong, and the content URL can be linked or downloaded directly. Both are gated on the same `{boundary}.invoice.attachments.read` permission — exposing the file under a weaker gate than the metadata that describes it would be backwards.

A **real multipart upload endpoint is generated.** It accepts a single
`IFormFile file` (plus an optional `description` form field) from a
`multipart/form-data` request, reads the file's stream and metadata, and binds
them onto the action: `FileContent = file.OpenReadStream()`,
`FileName = file.FileName`, `FileSize = file.Length`,
`ContentType = file.ContentType`. The endpoint calls `DisableAntiforgery()` and
`Accepts<IFormFile>("multipart/form-data")`, and enforces the attribute limits
at the HTTP boundary before invoking the action — returning `413` when the file
exceeds `MaxFileSizeBytes` and `415` when its extension is not in
`AllowedExtensions`. On success it returns `201 Created` with the new
attachment id.

### Permissions — `InvoiceAttachmentPermissions`

**The generated endpoints enforce these permissions themselves** — every route is gated on the constant for its operation. The constants are emitted so you can grant them by name:

- `Upload` = `{boundary}.invoice.attachments.upload`
- `Read` = `{boundary}.invoice.attachments.read`
- `Delete` = `{boundary}.invoice.attachments.delete`

(`{boundary}` is the boundary name in kebab-case, or `app` when no boundary is known; the entity segment is kebab-case too.)

### Thumbnails

With `ThumbnailMaxWidth` and `ThumbnailMaxHeight` both set, the upload decodes an image once, resizes it
within those bounds keeping the aspect ratio, and stores it beside the original in
`AttachmentBase.ThumbnailUri` — nullable, since a file that is not an image has none. It is served by
`DownloadThumbnailInvoiceAttachmentAction` at `GET /attachments/{attachmentId}/thumbnail`; the content
route never serves it. A file whose extension names an image format and whose bytes do not decode is
refused at upload.

⚠️ The derivation needs `Pragmatic.Imaging` referenced — a native library, so the deployed runtime
identifier must be one the package ships. Declared without it, nothing is generated and the build
reports **PRAG2651**.

## How the SG Wiring Works

- **Trigger / detection.** `[HasAttachments]` is detected by FQN `Pragmatic.Attachments.HasAttachmentsAttribute` (`shared/SourceGen/AttributeNames.cs`). The unified generator's `TraitDetector` records `HasAttachments` + the attribute options into a `TraitSet`; the dedicated `[HasAttachments]` pipeline in `TraitFeature` uses `ForAttributeWithMetadataName` + `AttachmentTraitTransform` to build the model.
- **Guards.** Entity / config / navigation are generated only when `HasPersistenceEFCore` is detected; actions, DTO, list query, and permissions are generated only when `HasActions` is also present. Endpoints come from injected `EndpointModel`s consumed by the Endpoints feature.
- **No `[Resource]` → diagnostic `PRAG2601`.** Without `[Resource(...)]` on the parent, no route segment can be derived, so endpoints are skipped and `PRAG2601` is reported.
- **Hint names.** Each artifact uses `VirtualFolderHints`: per-type `{Type}.Entity.g.cs` / `.Action.g.cs` / `.Dto.g.cs` / `.QueryClass.g.cs`, `{Parent}.AttachmentPermissions.g.cs`, and `EntityConfig.{Attachment}.g.cs` for the cross-assembly EF config.

## Operational notes

- **Server-derived `FileName`/`FileSize`/`ContentType`.** The generated upload
  endpoint binds these from the `IFormFile` provided by ASP.NET Core, and both
  the endpoint (`413`/`415`) and the generated `Upload{Parent}AttachmentAction`
  enforce the attribute limits (`MaxPerEntity`, `MaxFileSizeBytes`,
  `AllowedExtensions`). The values are still stored verbatim: the framework does
  **not** scan for hostile names (`..`, control chars, NUL) or verify that the
  declared content type matches the actual bytes.
- **Where to add stricter validation.** If you need name sanitization or
  magic-byte sniffing, add a `[PreProcessor]` on the upload action (or a custom
  endpoint) — the generated endpoint covers size/extension/limits, not content
  inspection.
- The Storage layer's path-safety check protects the on-disk container path, not
  the file-name field recorded on the entity.
- **Delete is a soft delete; the stored file is kept.**
  `Delete{Parent}AttachmentAction` sets `IsDeleted`/`DeletedAt`/`DeletedBy` on the
  metadata row and does not call `IFileStorage.DeleteAsync` — a soft delete is
  reversible, and removing the bytes would make a restore impossible.
- **Reclaiming the blobs is opt-in: `PurgeDeletedAfterDays`.** Set it to a positive
  number and the generator emits `Purge{Parent}AttachmentsJob`, a `[RecurringJob]`
  (schedule: `PurgeCron`) that queries with `IgnoreQueryFilters()` — the soft-delete
  filter hides exactly the rows it needs — then, per row, deletes the blob **first**
  and removes the row second. A row whose blob cannot be deleted is logged, skipped
  and retried next run, so one unreachable file neither blocks the others nor fails
  the job. Left at its default (`0`) nothing is generated and **storage keeps growing
  until your application reclaims it**; when a soft-deleted attachment is beyond
  recall is a business decision the attribute cannot make. See
  [Retention and purging](docs/concepts.md#retention-and-purging).

## Status

**Functional** within 1.0.0-alpha — the `[HasAttachments]` trait with its entity, actions, endpoints,
thumbnails and retention purge, tested end to end in the Showcase. See the [roadmap](../docs/ROADMAP.md).

## Documentation

Local docs:

- [Concepts](docs/concepts.md)
- [Getting Started](docs/getting-started.md)
- [Common Mistakes](docs/common-mistakes.md)
- [Troubleshooting](docs/troubleshooting.md)

Related modules:

- [Pragmatic.Storage](../Pragmatic.Storage/README.md)
- [Pragmatic.Comments](../Pragmatic.Comments/README.md)


## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Attachments is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
