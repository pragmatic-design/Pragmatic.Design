---
name: pragmatic-use-traits
description: Use when an entity needs comments, tags, attachments or notes; [HasComments], [HasTags], [HasAttachments], [HasNotes] generate the child entity, actions, endpoints and permissions instead of hand-writing them.
---

# Pragmatic Use Traits (Medium Blocks)

**Covers:** Add comments, tags, attachments or notes to any entity with one attribute ([HasComments], [HasTags], [HasAttachments], [HasNotes]); the generator emits the child entity, EF config, actions, endpoints, DTOs and permissions.

Medium Blocks add a complete capability to an entity from a single attribute. The source generator
produces the child entity, EF configuration (FK/indexes/soft-delete), CRUD + moderation actions with
invokers, a paged list query, DTO with projection, HTTP endpoints, and permission constants: 20+ files
you don't write.

## When to use

- An entity needs **comments** (threaded, moderated), **tags**, **attachments** (files), or **notes**.

## Packages (add the trait you need)

```xml
<PackageReference Include="Pragmatic.Comments" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Tags" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Attachments" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Notes" Version="1.0.0-alpha.1" />
```

The SG detects the attributes automatically, with no extra DI wiring.

## Core pattern

Decorate the entity (it needs `[Entity]`, `[Resource("...")]` for the route prefix, and its boundary):

```csharp
using Pragmatic.Comments;
using Pragmatic.Tags;
using Pragmatic.Attachments;

[Entity]
[Resource("reservations")]
[BelongsTo<BookingBoundary>]
[HasComments]
[HasTags]
[HasAttachments(AllowedExtensions = ".pdf,.jpg,.png", PurgeDeletedAfterDays = 30)]
public partial class Reservation : IEntity { /* ... */ }
```

Generated endpoints for Comments (Notes are the same five routes with `notes` in place of `comments`):

```
POST   /api/booking/reservations/{id}/comments                  → add
GET    /api/booking/reservations/{id}/comments                  → list (paged, newest first)
GET    /api/booking/reservations/{id}/comments/{cid}            → get one
PUT    /api/booking/reservations/{id}/comments/{cid}            → update      (AllowEditing only)
DELETE /api/booking/reservations/{id}/comments/{cid}            → soft-delete
PUT    /api/booking/reservations/{id}/comments/{cid}/moderation → moderate    (RequireApproval only)
GET    /api/booking/reservations/{id}/comments/pending          → queue       (RequireApproval only)
```

Tags are **not** analogous: there is no update and no get-one, and the delete key is the tag id:

```
POST   /api/booking/reservations/{id}/tags          → add (body is a bare JSON string)
GET    /api/booking/reservations/{id}/tags          → list (paged)
DELETE /api/booking/reservations/{id}/tags/{tagId}  → unlink; the tag row goes too when nothing else uses it
```

Attachments add a content route on top of the metadata one:

```
POST   /api/booking/reservations/{id}/attachments               → upload (multipart, field name "file")
GET    /api/booking/reservations/{id}/attachments/{aid}         → metadata (JSON)
GET    /api/booking/reservations/{id}/attachments/{aid}/content → the file itself
DELETE /api/booking/reservations/{id}/attachments/{aid}         → soft-delete (blob kept)
```

Both GETs need the same `…attachments.read` permission, both filter on the parent id in the route as
well as the attachment id (so a foreign attachment id is a 404, not a download), and the metadata DTO
does **not** expose `StorageUri`; use `/content`.

Delete is a soft delete and never removes the blob. `PurgeDeletedAfterDays = N` (default `0` = off)
generates a `[RecurringJob]` (schedule via `PurgeCron`, default `"0 3 * * *"`) that deletes the blob
first and the row second, skipping (and retrying next run) any blob it cannot reach. It needs
`Pragmatic.Jobs` referenced.

`ThumbnailMaxWidth` + `ThumbnailMaxHeight` (both, default `0` = off) make the upload derive an image's
thumbnail once, stored beside it (`ThumbnailUri`, nullable) and served at `…/attachments/{aid}/thumbnail`.
It needs `Pragmatic.Imaging` (native) referenced; without it nothing is generated and the build
reports **PRAG2651**.

A generated boundary interface (e.g. `IBookingReservationCommentsActions`, named
`I{Boundary}{SubBoundary}Actions`) lets you call the same operations in-process.

Every generated route enforces its own permission: a caller without it gets 403, so grant them
explicitly. `[HasComments]` and `[HasTags]` accept an optional `ICommentPolicy<TId>` /
`ITagPolicy<TId>` registered with a plain `AddScoped`: the hooks run inside the generated actions,
and a `CommentRejectedError` returned from one surfaces as 422 with its reason.

Editing or deleting a comment or note requires being its author, or holding the trait's `moderate`
permission. Uploads refuse extensions a browser executes (`.html`, `.svg`, `.js`, …) unless
`AllowedExtensions` names them explicitly.
`[HasAttachments]` integrates with `pragmatic-use-storage` for the file bytes. Permissions follow the
`{boundary}.{resource}.comments.{action}` convention; grant them via `pragmatic-use-authorization`.

## Options, per trait

Every trait also takes `SubBoundary`, the name of its group on the boundary interface (default
`{Parent}Comments`, `{Parent}Tags`, …).

| Trait | Option (default) | What it does |
|---|---|---|
| `[HasComments]` | `MaxLength` (2000) | longer content is a 400 naming the limit, not a truncation |
| | `AllowReplies` (true) | `ReplyToId` on add; replies are stored flat beside the others |
| | `AllowEditing` (true), `EditWindowMinutes` (-1 = always; 0 = never after creation) | `false` removes the update action and its `PUT` |
| | `RequireApproval` (false) | new comments start `PendingApproval`; a query filter hides everything not `Visible` |
| | `SupportInternalNotes` (false) | adds `Visibility` (`Public` / `Internal`) to the add body and the DTO |
| `[HasNotes]` | `MaxLength` (4000), `AllowEditing`, `EditWindowMinutes` | staff notes: no threading, no moderation |
| `[HasTags]` | `MaxPerEntity` (50; 0 = unlimited) | enforced in a serializable transaction: the loser of a race for the last slot gets 409, retry it |
| | `AllowCustom` (true) | `false` = curated taxonomy: an unknown value is a 404, never created |
| | `CaseSensitive` (false) | "Urgent" and "urgent" are one tag unless true |
| | `Scope` (the parent type name) | set the same value on several entities to share one vocabulary |
| `[HasAttachments]` | `MaxPerEntity` (20), `MaxFileSizeBytes` (10 MB) | 0 = no limit; the count holds under concurrency (409) |
| | `AllowedExtensions` ("" = all but browser-executable) | comma-separated, case-insensitive, dot optional |
| | `Container` (parent type name, lower-case) | the storage container |
| | `PurgeDeletedAfterDays`, `PurgeCron`, `ThumbnailMaxWidth/Height` | above |

**Moderation hides pending comments from readers**: the list returns only `Visible` ones. The moderator
has the queue, `GET …/comments/pending` (`ListPending{Entity}CommentsAction`, `moderate` permission,
oldest first, `page`/`pageSize`), which lifts only the named `Moderation` filter (tenant and soft delete
stay) and hides a pending `Internal` note from a moderator without `view-internal`.

**`SupportInternalNotes` is enforced by one permission**, generated as
`{Entity}CommentPermissions.ViewInternal` (`{boundary}.{entity}.comments.view-internal`). Without it a
caller does not see comments marked `Internal` in the list (a generated row filter), gets `404` reading one
by id, and gets `403` adding one. Grant it to staff; `comments.read` alone is the public thread. ⚠️ A
caller must also be able to see the **parent** (a thread of a row one cannot see is empty whatever the
visibility), so test the internal case with a reader who sees the parent, or the test passes on the
parent's filter instead.

⚠️ **A curated taxonomy has no endpoint to create it.** With `AllowCustom = false` the tags are seeded
by the application (the generated `Tag` entity, through the DbContext or a seeding step), and unlinking
keeps them at zero usage. With `AllowCustom = true` a tag row nothing links any more is deleted with its
last link, so a typo does not stay in the vocabulary.

**Notes are staff-only by permission, not by construction**: the routes are the comments' routes with
`notes`, and whoever holds `notes.read` reads them. Grant it to staff roles only.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase example application (code
that compiles and that `Showcase.IntegrationTests` exercises) and kept identical to it by the gate:
comments, tags and attachments with purge and thumbnails on one entity, and notes, moderated comments and
a tag limit on another.

`ITagPolicy` is used by no tested application yet, so there is no example of it here: the sections above
are the reference.
