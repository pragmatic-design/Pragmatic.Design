# Pragmatic.Notes

Add internal staff notes to any Pragmatic entity with a single attribute.

`Pragmatic.Notes` is the "internal annotation" sibling of `Pragmatic.Comments`: flat notes, no threading, no anonymous authors, and no public-facing discussion model.

## The Problem

Internal notes are common in line-of-business systems, but they still require repetitive boilerplate:

- child entity and FK
- CRUD actions
- endpoint mapping
- auditing fields
- rules around editing and visibility

Most projects end up rebuilding the same pattern repeatedly.

## The Solution

One attribute activates the note feature:

```csharp
using Pragmatic.Notes;
using Pragmatic.Persistence.Entity;

[Entity]
[Resource("reservations")]
[HasNotes<Reservation>]
public partial class Reservation : IEntity
{
    public string GuestName { get; set; } = "";
}
```

`HasNotesAttribute<TParent>` is generic (arity 1): pass the parent entity type, e.g. `[HasNotes<Reservation>]`. `[Resource(...)]` is required for endpoints; without it the generator reports `PRAG2601` and skips endpoint generation.

The generator creates:

- a typed `{Parent}Note` entity derived from `NoteBase<TEntityId>`
- EF configuration
- add, update, delete, and get-by-id actions
- note endpoints under the parent resource
- a `Notes` navigation on the parent entity

## Installation

```xml
<PackageReference Include="Pragmatic.Notes" />
```

Typical consumers also reference:

- `Pragmatic.Persistence`
- `Pragmatic.Endpoints` if HTTP endpoints are desired

## Quick Start

```csharp
[Entity]
[Resource("reservations")]
[HasNotes<Reservation>(MaxLength = 4000, EditWindowMinutes = 60)]
public partial class Reservation : IEntity
{
    public string GuestName { get; set; } = "";
}
```

Generated surface typically includes:

- add note
- get note by id
- update note content
- delete or soft-delete a note

## Attribute Options

| Option | Default | Description |
|--------|---------|-------------|
| `MaxLength` | `4000` | Maximum note content length. |
| `AllowEditing` | `true` | Whether authors can edit their own notes. |
| `EditWindowMinutes` | `-1` | Minutes after creation in which edits are allowed. `-1` means no limit. |
| `SubBoundary` | `{Parent}Notes` | Override generated sub-boundary name. |

## Note Model

Generated notes derive from `NoteBase<TEntityId>` and include:

- `ParentEntityId`
- `Content`
- `AuthorId` and `AuthorName`
- `IsEdited`
- `CreatedAt`, `UpdatedAt`, `UpdatedBy`
- soft-delete fields

Unlike comments, notes are always internal and always require an author.

## Generated Endpoints

For `[HasNotes<Reservation>]` on `Reservation` (`[Resource("reservations")]`), endpoints are generated under `/api/{boundary}/reservations/{reservationId}/notes` (OpenAPI tag `Notes`). `{boundary}` is the lowercased boundary name from `[BelongsTo<TBoundary>]`, or `v1` if none. Generation requires `HasPersistenceEFCore` + `HasActions`; end-to-end compilation tests confirm `[HasNotes]` produces working code.

| Verb | Route | Maps to | Request body | Response |
|------|-------|---------|--------------|----------|
| `POST` | `/notes` | `AddReservationNoteAction` | `{ "content": string }` | `201`, new note `Guid` |
| `GET` | `/notes` | `ListReservationNotesQuery` (paged) |none| `ReservationNoteDto[]` (paged; `Page`/`PageSize`) |
| `GET` | `/notes/{noteId}` | `GetReservationNoteAction` |none| `ReservationNoteDto` |
| `PUT` | `/notes/{noteId}` | `UpdateReservationNoteAction` | `{ "content": string }` | `204` |
| `DELETE` | `/notes/{noteId}` | `DeleteReservationNoteAction` |none| `204` (soft-delete) |

`ReservationNoteDto` shape: `Id`, `ReservationId`, `Content`, `AuthorId`, `AuthorName`, `IsEdited`, `CreatedAt`, `UpdatedAt?`. The route parameter `{reservationId}` binds to the action/query `ReservationId` property; `{noteId}` binds to `NoteId`.

## Authorization and Visibility Constraints

Two layers apply.

**Permission constants**: `ReservationNotePermissions` (static class) exposes:

- `Create` = `{boundary}.reservation.notes.create`
- `Read` = `{boundary}.reservation.notes.read`
- `Update` = `{boundary}.reservation.notes.update`, only with `AllowEditing` (the default); without it
  there is no update action or route, so no constant either
- `Delete` = `{boundary}.reservation.notes.delete`
- `Moderate` = `{boundary}.reservation.notes.moderate`, always emitted

(Boundary and entity segments are kebab-case; the boundary segment is `app` when the parent has no `[BelongsTo<TBoundary>]`; note the route falls back to `v1` in that same case, so prefix and route segment differ.) **The generated endpoints enforce these permissions themselves**: each route is gated on the constant for its operation, so a caller without it receives 403. The constants exist so you can grant them by name instead of retyping the string.

**Author-ownership enforced in the action body**: independent of permissions, `UpdateReservationNoteAction` checks `note.AuthorId == ICurrentUser.Id` and returns `ForbiddenError` otherwise, so only the original author can edit a note. When `EditWindowMinutes > 0`, edits past `CreatedAt + EditWindowMinutes` also return `ForbiddenError`. On a successful edit the action sets `IsEdited = true`, `UpdatedAt`, and `UpdatedBy`. `Add` records `AuthorId`/`AuthorName` from `ICurrentUser`; `Delete` is a soft-delete (`IsDeleted`/`DeletedAt`/`DeletedBy`) and reads exclude soft-deleted rows via the EF query filter.

Both `Update` and `Delete` require the caller to be the author **or** to hold
`{boundary}.{entity}.notes.moderate`. Without the moderate permission, holding `notes.update` or
`notes.delete` alone is not enough to touch someone else's note.

## Relationship to Pragmatic.Comments

- `Pragmatic.Comments` is for public or customer-facing discussion
- `Pragmatic.Notes` is for staff-only internal annotations

Use one or both depending on the domain.

## Status

**Functional** within 1.0.0-alpha: the `[HasNotes<TParent>]` trait with its entity, actions and
endpoints, tested end to end in the Showcase. See the [roadmap](../docs/ROADMAP.md).

## Documentation

Local docs:

- [Concepts](docs/concepts.md)
- [Getting Started](docs/getting-started.md)
- [Common Mistakes](docs/common-mistakes.md)
- [Troubleshooting](docs/troubleshooting.md)

Related modules:

- [Pragmatic.Comments](../Pragmatic.Comments/README.md)

## Requirements

- .NET 10.0+
- `Pragmatic.Persistence.EFCore` and `Pragmatic.Actions` (endpoints also need `Pragmatic.Endpoints`)
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](../README.md) ecosystem. See [Licensing](../docs/LICENSING.md).
Pragmatic.Notes is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
