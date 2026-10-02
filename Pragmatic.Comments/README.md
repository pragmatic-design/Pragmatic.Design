# Pragmatic.Comments

Add threaded, moderated comments to any entity with a single attribute.

## The Problem

Adding comments to a domain entity requires creating a child entity, EF configuration, FK relationships, CRUD actions, HTTP endpoints, permissions, and DTOs. For each entity that needs comments, you repeat 15+ files of boilerplate:

```csharp
// Without Pragmatic.Comments: 15+ files per entity
public class ReservationComment { ... }           // Entity
public class ReservationCommentConfig { ... }     // EF config
public class AddReservationCommentAction { ... }  // Create action
public class UpdateReservationComment { ... }     // Update action
public class DeleteReservationComment { ... }     // Delete action
public class ListReservationComments { ... }      // Query
public class ReservationCommentDto { ... }        // DTO
public class CommentEndpoints { ... }             // HTTP handlers
// + Invokers, SetDependencies, permissions, request DTOs...
```

## The Solution

One attribute, zero boilerplate. The source generator creates everything:

```csharp
[Entity]
[Resource("reservations")]
[HasComments]                    // <-- this is all you write
public partial class Reservation : IEntity { ... }
```

The SG generates **20+ files**: entity, EF config with FK/indexes/soft-delete, Add/GetById/Update/Delete/Moderate actions with Invokers, paged list query, DTO with Projection, HTTP endpoints, and permission constants.

## Installation

```xml
<PackageReference Include="Pragmatic.Comments" />
```

No additional configuration needed. The SG detects `[HasComments]` automatically.

## Quick Start

### 1. Add the attribute

```csharp
using Pragmatic.Comments;
using Pragmatic.Persistence.Entity;

[Entity]
[Resource("reservations")]
[HasComments]
[BelongsTo<BookingBoundary>]
public partial class Reservation : IEntity { ... }
```

### 2. Use the generated endpoints

```
POST   /api/booking/reservations/{id}/comments                  → Add comment
GET    /api/booking/reservations/{id}/comments                  → List (paged, newest first)
GET    /api/booking/reservations/{id}/comments/{cid}            → Get detail
PUT    /api/booking/reservations/{id}/comments/{cid}            → Update
DELETE /api/booking/reservations/{id}/comments/{cid}            → Soft-delete
PUT    /api/booking/reservations/{id}/comments/{cid}/moderation → Moderate (RequireApproval only)
GET    /api/booking/reservations/{id}/comments/pending          → Moderation queue (RequireApproval only)
```

Every route is gated on its own permission (see *Generated Permissions*): a caller without it
gets 403. The list is ordered by `CreatedAt` descending unless the caller passes `createdAtSort`.

### 3. Use via boundary interface

```csharp
// Actions in IBookingReservationCommentsActions sub-boundary
var result = await actions.ReservationComments.AddReservationComment(
    new AddReservationCommentAction
    {
        ReservationId = reservationId,
        Content = "Great stay!",
    }, ct);
```

## Configuration

All options are on the `[HasComments]` attribute:

| Option | Default | Description |
|--------|---------|-------------|
| `MaxLength` | 2000 | Maximum content length |
| `AllowReplies` | true | Enable threaded replies (self-referencing FK) |
| `AllowEditing` | true | Authors can edit their own comments. `false` suppresses the Update action and its `PUT` endpoint entirely |
| `EditWindowMinutes` | -1 (no limit) | Time limit for editing after creation |
| `RequireApproval` | false | New comments start as PendingApproval |
| `SupportInternalNotes` | false | Enable Public/Internal visibility |
| `SubBoundary` | `{Entity}Comments` | Override sub-boundary name |

## Comment Entity (CommentBase)

Every generated comment entity inherits `CommentBase<TEntityId>`:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | Guid | Primary key |
| `ParentEntityId` | TEntityId | FK to parent (abstract, concrete generated) |
| `Content` | string | Comment text |
| `AuthorId` | string? | User who created |
| `AuthorName` | string? | Display name snapshot |
| `ReplyToId` | Guid? | Parent comment for threading |
| `Status` | CommentStatus | Visible, PendingApproval, Rejected, Hidden |
| `Visibility` | CommentVisibility | Public or Internal |
| `IsEdited` | bool | True after first edit |
| `CreatedAt` | DateTimeOffset | Creation timestamp |
| `UpdatedAt` | DateTimeOffset? | Last edit timestamp |
| `UpdatedBy` | string? | Who last edited |
| `IsDeleted` | bool | Soft-delete flag |
| `DeletedAt` | DateTimeOffset? | When deleted |
| `DeletedBy` | string? | Who deleted |
| `Metadata` | string? | JSON extensibility field |

## ICommentPolicy (Hooks)

Implement `ICommentPolicy<TEntityId>` to customize behavior:

```csharp
public class ReservationCommentPolicy : ICommentPolicy<Guid>
{
    // Every member is a default interface method: override only what you need.
    // Returning a failure surfaces YOUR error to the caller — CommentRejectedError
    // maps to 422 and carries the reason, so the client learns why it was refused.
    public Task<VoidResult<CommentRejectedError>> CanAddAsync(
        Guid reservationId, string content, string? authorId, CancellationToken ct = default)
        => Task.FromResult(content.Contains("spam", StringComparison.OrdinalIgnoreCase)
            ? VoidResult<CommentRejectedError>.Failure(CommentRejectedError.Because("Looks like spam."))
            : VoidResult<CommentRejectedError>.Success());

    public Task OnAddedAsync(Guid reservationId, Guid commentId, CancellationToken ct = default)
    {
        // Send notification, update counter, etc. Runs BEFORE the commit.
        return Task.CompletedTask;
    }

    public Task<VoidResult<CommentRejectedError>> CanDeleteAsync(
        Guid commentId, string? requesterId, CancellationToken ct = default)
        => Task.FromResult(VoidResult<CommentRejectedError>.Success());

    public Task<VoidResult<CommentRejectedError>> CanEditAsync(
        Guid commentId, string? requesterId, CancellationToken ct = default)
        => Task.FromResult(VoidResult<CommentRejectedError>.Success());
}
```

Register in DI:
```csharp
services.AddScoped<ICommentPolicy<Guid>, ReservationCommentPolicy>();
```

The policy is resolved from the application container, so a plain `AddScoped` is all it takes.
If none is registered the defaults apply: **editing and deleting both require the caller to be the
author, or to hold `{boundary}.{entity}.comments.moderate`.** The moderate permission is always
emitted, precisely so that someone can act on content they did not write.

### Moderation

With `RequireApproval = true` a new comment is created as `PendingApproval` and **is not readable**
until a moderator approves it — reads only return `Visible` comments. Approve, reject or hide one
with `PUT .../comments/{cid}/moderation` (body: the new status), gated on
`{boundary}.{entity}.comments.moderate`. A rejected comment stays reachable to that endpoint, so a
rejection can be undone. What awaits approval is listed by `GET .../comments/pending` — the moderation
queue, oldest first, paged with `page`/`pageSize`, under the same permission.

### Internal comments

With `SupportInternalNotes = true` the POST body accepts a `visibility` field (`Public` or
`Internal`) and the DTO returns it. Internal comments belong to whoever holds
`{boundary}.{entity}.comments.view-internal` (`…CommentPermissions.ViewInternal`): the generated list
filters them out for everyone else, reading one by id answers 404, and adding one without the permission
answers 403.

## Generated Permissions

```csharp
ReservationCommentPermissions.Create   // "booking.reservation.comments.create"
ReservationCommentPermissions.Read     // "booking.reservation.comments.read"
ReservationCommentPermissions.Update   // "booking.reservation.comments.update"  (AllowEditing only)
ReservationCommentPermissions.Delete   // "booking.reservation.comments.delete"
ReservationCommentPermissions.Moderate // "booking.reservation.comments.moderate" (always — see below)
ReservationCommentPermissions.ViewInternal // "booking.reservation.comments.view-internal" (SupportInternalNotes only)
```

The generated endpoints enforce these themselves — the constants exist so you can grant them by
name rather than retyping the string. `Moderate` is emitted even without `RequireApproval`: it gates
the moderation endpoint when there is one, and it is also what lets someone edit or delete a comment
they did not write.

## What Gets Generated

From a single `[HasComments]` on an entity:

| Artifact | Hint Name | Description |
|----------|-----------|-------------|
| Entity class | `{Entity}Comment.Entity.g.cs` | Concrete entity with typed FK |
| EF Config | `EntityConfig.{Entity}Comment.g.cs` | PK, FK, indexes, soft-delete filter |
| Parent navigation | `{Entity}.TraitNavigations.g.cs` | `ICollection<{E}Comment> Comments` |
| Add action | `Add{E}CommentAction.Action.g.cs` | + Invoker + SetDependencies |
| GetById action | `Get{E}CommentAction.Action.g.cs` | + Invoker + SetDependencies |
| Update action | `Update{E}CommentAction.Action.g.cs` | + Invoker + SetDependencies |
| Delete action | `Delete{E}CommentAction.Action.g.cs` | + Invoker + SetDependencies |
| Moderate action | `Moderate{E}CommentAction.Action.g.cs` | `RequireApproval` only |
| Moderation queue | `ListPending{E}CommentsAction.Action.g.cs` | `RequireApproval` only |
| List query | `List{E}CommentsQuery.QueryClass.g.cs` | + Apply() from QueryFeature |
| DTO | `{E}CommentDto.Dto.g.cs` | With Projection expression |
| Permissions | `{E}.CommentPermissions.g.cs` | Permission constants |
| Metadata | `_Metadata.TraitEntities.g.cs` | For host DbContext discovery |
| Endpoints | `*.Endpoint.g.cs` | POST/GET/PUT/DELETE, plus the moderation `PUT` and the pending `GET` with `RequireApproval`; no update `PUT` with `AllowEditing = false` |

## Requirements

- .NET 10+
- `Pragmatic.Abstractions` (transitive)
- `Pragmatic.SourceGenerator` (analyzer)
- `[Resource("segment")]` on entity for endpoint generation
- `[Entity]` on entity

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Comments is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
