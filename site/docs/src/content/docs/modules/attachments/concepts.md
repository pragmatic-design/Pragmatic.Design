---
title: "Concepts"
description: "`Pragmatic.Attachments` is a trait package for parent-child file metadata."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Attachments/docs/concepts.md
sidebar:
  order: 1
---
`Pragmatic.Attachments` is a trait package for parent-child file metadata.

## Mental Model

The parent entity stays your domain aggregate. The generated attachment entity stores metadata about files associated with that parent:

- original file name
- content type
- size
- storage URI
- uploader and timestamps

The actual file bytes live in `Pragmatic.Storage`, not in the attachment entity itself.

## Generated Shape

From a single `[HasAttachments]`, the consuming project gets:

- a typed `{Parent}Attachment` entity
- EF configuration and FK
- actions for upload, read-metadata (`GetById`), download (content), and delete
- attachment endpoints under the parent resource
- a navigation collection on the parent
- with `PurgeDeletedAfterDays` set, a `[RecurringJob]` that reclaims expired blobs

## Reading: metadata and content

The read side is two endpoints:

| Route | Returns |
|---|---|
| `GET /api/{boundary}/{parents}/{parentId}/attachments/{attachmentId}` | JSON metadata |
| `GET /api/{boundary}/{parents}/{parentId}/attachments/{attachmentId}/content` | the file |

Both are gated on the same `{boundary}.{parent}.attachments.read` permission, both skip
soft-deleted rows, and both load the attachment filtered on **the parent id in the route as
well as the attachment id**. That second filter is what makes the id in the URL useless on
its own: a caller allowed to read one parent's attachments cannot reach another parent's by
substituting an id.

The content response carries `Content-Type` from the recorded `ContentType` and
`Content-Disposition` from `FileName`. If the metadata row exists but the blob does not (it was
removed out of band, a bucket lifecycle rule, a database restored from an older snapshot),
the response is **404**, not 500: a missing representation is an expected state of the
resource, and no retry would help.

The metadata DTO does **not** include `StorageUri`. It names the provider and the bucket
layout, and with some providers it is a directly reachable URL; the content endpoint makes
exposing it unnecessary.

## Retention and purging

Deleting an attachment is a **soft delete**. `Delete{Parent}AttachmentAction` sets
`IsDeleted`, `DeletedAt` and `DeletedBy` on the metadata row; the file stays in
`IFileStorage`. That is deliberate: a soft delete is reversible, so removing the bytes
would turn a recoverable operation into permanent data loss.

Reclaiming the storage is a separate, scheduled decision:

```csharp
[HasAttachments(PurgeDeletedAfterDays = 30)]          // blobs reclaimed 30 days after deletion
public partial class Invoice : IEntity { }
```

That generates `Purge{Parent}AttachmentsJob`, a `[RecurringJob]` running at `PurgeCron`
(default `"0 3 * * *"`, daily at 03:00) which, for every attachment soft-deleted longer
ago than the window:

1. deletes the blob from `IFileStorage`;
2. only then removes the metadata row.

That order is not cosmetic. The row is the only pointer to the file, so dropping it first
and then failing leaks a file nobody can find again; this way a failure just leaves the row
for the next run, and the storage delete is idempotent so the retry costs nothing. A single
unreachable blob is logged and skipped: it does not stop the other attachments from being
purged and it does not fail the job.

**The default is no purge.** `PurgeDeletedAfterDays` left at `0` generates no job at all, so
**stored files accumulate until the application removes them**. How long a soft-deleted
attachment must remain recoverable is a business/compliance decision the trait cannot make
for you; if your answer is "it depends, per record", keep the option off and write the job
yourself: delete the blob first and the row second, and remember `IgnoreQueryFilters()`,
because the generated EF configuration applies `HasQueryFilter(e => !e.IsDeleted)` and hides
exactly the rows you are looking for.

One more detail: the FK to the parent is `OnDelete(Cascade)`, so deleting a parent removes
its attachment **rows**; the files behind them are not touched, and cascade-removed rows
are gone before any purge job can see them.

## Dependency Model

The trait relies on other Pragmatic building blocks for different concerns:

- `Pragmatic.Persistence` for entity shape and EF integration
- `Pragmatic.Storage` for physical file storage
- `Pragmatic.Endpoints` for generated HTTP endpoints

