# Pragmatic.Comments.Samples

Runnable console samples for the **runtime** surface of `Pragmatic.Comments`.

> [!NOTE]
> `Pragmatic.Comments` is a **trait package**. Its `[HasComments]` **source
> generator IS implemented** — in a real Pragmatic host it emits the typed
> `{Parent}Comment` entity, an EF Core configuration, the `Add`/`Update`/`Delete`
> actions, the REST endpoints (`/api/.../{parentId}/comments`) and the permission
> constants. A plain console app does **not** run that host SG pipeline, so these
> samples exercise only the hand-written runtime types that execute without it.
> A `[HasComments]`-annotated entity is included purely as a declaration (the
> attribute compiles) to show how a consumer opts in.

## Run

```bash
dotnet run --project Pragmatic.Comments/samples/Pragmatic.Comments.Samples
```

## What is demonstrated (runtime surface)

| Sample | File | Surface |
|--------|------|---------|
| 1 | `HasCommentsOptionsSample.cs` | `HasCommentsAttribute` options + defaults (`MaxLength`, `AllowReplies`, `AllowEditing`, `EditWindowMinutes`, `RequireApproval`, `SupportInternalNotes`, `SubBoundary`); free-form, moderated, locked-down and edit-window variants |
| 2 | `CommentEntitySample.cs` | A concrete `CommentBase<Guid>` entity; `Id` ⇄ `PersistenceId` forwarding; content/author/threading fields; `CommentStatus` lifecycle; `CommentVisibility` (public vs internal note); edit + soft-delete columns |
| 3 | `CommentPolicySample.cs` | `ICommentPolicy<Guid>` default-interface-method behavior (`CanAddAsync`, `CanDeleteAsync`, `OnAddedAsync`) + a custom policy rejecting via `CommentRejectedError` (block-list, delete guard) |

## Not covered (depends on the host `[HasComments]` SG)

- SG-generated `{Parent}Comment` entity, EF config and parent navigation collection
- SG-generated `Add`/`Update`/`Delete` actions wired to the registered `ICommentPolicy`
- SG-generated HTTP endpoints and the `comments.view-internal` permission

These require the host source-generation pipeline and are intentionally out of scope for a runnable console sample.
