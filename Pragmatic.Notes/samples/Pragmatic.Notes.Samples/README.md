# Pragmatic.Notes.Samples

Runnable console samples for the **existing** runtime surface of `Pragmatic.Notes`.

```bash
dotnet run --project Pragmatic.Notes/samples/Pragmatic.Notes.Samples
```

## What is covered

| Sample | Surface exercised |
|--------|-------------------|
| `HasNotesOptionsSample` | `HasNotesAttribute<TParent>` options: `MaxLength`, `AllowEditing`, `EditWindowMinutes`, `SubBoundary` — including the `EditWindowMinutes` semantics (`-1` = no limit, `0` = no window, `> 0` = N minutes after creation), plus the `AllowEditing = false` master-switch case. |
| `NoteBaseEntitySample` | A concrete entity deriving from `NoteBase<TEntityId>`: `Id` / `PersistenceId` (passthrough), `ParentEntityId`, `Content`, `AuthorId`, `AuthorName`, `IsEdited`, `CreatedAt` / `UpdatedAt` / `UpdatedBy`, and the soft-delete fields `IsDeleted` / `DeletedAt` / `DeletedBy`. |

## What these samples do not cover

The `[HasNotes<T>]` source generator emits the note entity, its EF Core configuration, the four CRUD
actions, the REST endpoints and the `Notes` navigation property **inside the consuming project**. A
console application has no generation pipeline for a boundary, so none of that appears here. For the
generated surface end to end — routes, permissions, soft delete, edit window — see the `Guest` entity
in `examples/showcase` and the note integration tests that exercise it against a real database.
