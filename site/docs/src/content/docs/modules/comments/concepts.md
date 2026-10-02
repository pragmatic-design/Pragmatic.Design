---
title: "Concepts"
description: "Every entity that needs user comments requires the same boilerplate: a child entity with FK, EF configuration, CRUD actions, DTOs, endpoints, and permissions. T"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Comments/docs/concepts.md
sidebar:
  order: 1
---
## The Problem

Every entity that needs user comments requires the same boilerplate: a child entity with FK, EF configuration, CRUD actions, DTOs, endpoints, and permissions. This is 15-20 files of repetitive code per entity.

## The Solution

`Pragmatic.Comments` is a **trait plugin**: add `[HasComments]` to any entity and the source generator produces everything. No manual wiring, no base classes to configure.

## How It Works

```
[HasComments] on entity
    ↓ ForAttributeWithMetadataName (TraitFeature)
CommentTraitTransform.Transform()
    ↓ CommentTraitModel
    ↓
├── CommentEntityTemplate        → ReservationComment.g.cs
├── CommentEntityConfigTemplate  → EntityConfig.ReservationComment.g.cs
├── ParentTraitNavigationTemplate → Reservation partial (ICollection<> Comments)
├── CommentActionsTemplate       → Add/GetById/Update/Delete actions
├── CommentDtoTemplate           → ReservationCommentDto with Projection
├── CommentListQueryTemplate     → ListReservationCommentsQuery
├── CommentPermissionsTemplate   → Permission constants
├── TraitEntityMetadataTemplate  → Assembly metadata for host DbContext
└── TraitEndpointModelBuilder    → EndpointModels → EndpointsFeature handlers
```

### Pipeline Injection Pattern

The SG cannot see its own output in the same Roslyn pass. So trait-generated actions and endpoints are injected **programmatically** into the existing pipelines:

- **ActionModel** → `ActionsFeature.Register(traitActions:)` → Invoker + SetDependencies
- **QueryModel** → `QueryFeature.Register(resourceQueries:)` → Apply() + ToSpecification()
- **EndpointModel** → `EndpointsFeature.Register(programmaticEndpoints:)` → HTTP handlers

This means trait actions get the **full pipeline**: DI, UnitOfWork, validation, activity tracing — identical to hand-written `[DomainAction]` classes.

## Entity Architecture

Each parent entity gets its own comment table — no shared polymorphic table:

```
Reservations          ReservationComments
┌──────────┐         ┌─────────────────────┐
│ Id (PK)  │◄────────│ ReservationId (FK)  │
│ ...      │         │ PersistenceId (PK)  │
└──────────┘         │ Content             │
                     │ AuthorId            │
                     │ ReplyToId (self-FK) │
                     │ Status (varchar)    │
                     │ IsDeleted           │
                     └─────────────────────┘
```

Benefits: full referential integrity, cascade delete, no EntityType/EntityId polymorphism, independent indexes per table.

## Threading Model

When `AllowReplies = true` (default), comments support nested threading via `ReplyToId` self-referencing FK:

```
Comment A (ReplyToId = null)        ← top-level
├── Comment B (ReplyToId = A.Id)    ← reply to A
│   └── Comment C (ReplyToId = B.Id) ← reply to B
└── Comment D (ReplyToId = A.Id)    ← another reply to A
```

The self-FK uses `DeleteBehavior.Restrict` to prevent orphaned replies.

## Moderation

When `RequireApproval = true`:
- New comments start with `Status = PendingApproval`
- **Reads return only `Visible` comments** — a pending comment is invisible until approved, and a
  rejected or hidden one disappears again. That is the whole point of the option: without it,
  moderation would announce itself while showing everyone the unmoderated text.
- A `Moderate{Entity}CommentAction` is generated, exposed as
  `PUT .../comments/{commentId}/moderation` and gated on `{boundary}.{entity}.comments.moderate`
- That action reads **through** the query filter (`IgnoreQueryFilters`), so a moderator can still
  reach a pending or rejected comment to change its status. Soft-deleted comments stay out of
  reach: undeleting is not moderation.
- A `ListPending{Entity}CommentsAction` is the moderation queue, exposed as
  `GET .../comments/pending` under the same permission: the pending comments, oldest first, paged. It
  lifts only the named `Moderation` filter — tenant and soft delete stay — and applies the
  internal-visibility rule itself, since it reads the table directly.

## ICommentPolicy Lifecycle

```
Add Comment:
    1. Resolve ICommentPolicy<TId> from the APPLICATION container (optional)
    2. policy.CanAddAsync() → on failure the policy's own error is returned to the caller,
       so a CommentRejectedError surfaces as 422 with its reason
    3. Create entity, set fields from ICurrentUser + IClock
    4. _db.Set<T>().Add(comment)
    5. policy.OnAddedAsync() → notification hook (runs BEFORE the commit)

Update Comment:
    1. policy.CanEditAsync() → on failure the policy's error is returned
    2. Fallback: author-only check if no policy
    3. Edit window enforcement if configured
    4. Set Content, IsEdited, UpdatedAt, UpdatedBy

Delete Comment:
    1. policy.CanDeleteAsync() → on failure the policy's error is returned
    2. Soft-delete: IsDeleted, DeletedAt, DeletedBy
```

## Sub-Boundary Grouping

Comment actions are grouped in a sub-boundary interface:

```csharp
public interface IBookingActions
{
    IBookingReservationCommentsActions ReservationComments { get; }
    // ...
}

public interface IBookingReservationCommentsActions
{
    Task<Result<Guid, IError>> AddReservationComment(...);
    Task<VoidResult<IError>> UpdateReservationComment(...);
    Task<VoidResult<IError>> DeleteReservationComment(...);
    // ...
}
```

Customizable via `[HasComments(SubBoundary = "CustomName")]`.

## DbContext Integration

The SG generates assembly metadata (`_Metadata.TraitEntities.g.cs`) that the host reads at compile time. The host's `DbContextFeature`:
1. Reads trait entity metadata from referenced assemblies
2. Adds `DbSet<ReservationComment>` to BoundaryDbContext + MigrationDbContext
3. Applies the `ReservationCommentEntityConfig` (generated in module, public)

Schema metadata also includes the trait entity with resolved properties for `Pragmatic.Migrations` table creation.
