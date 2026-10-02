# Examples — pragmatic-use-traits

Copied from `examples/showcase/src/Showcase.Booking`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Reservations/Reservation.cs`](Reservations/Reservation.cs) | `[HasComments]`, `[HasTags]`, and `[HasAttachments]` with allowed extensions, a purge after 30 days and thumbnails, on an entity with its `[Resource]` |
| [`Guests/Guest.cs`](Guests/Guest.cs) | `[HasNotes]`, moderated comments with internal notes (`RequireApproval`, `SupportInternalNotes`), and a per-entity tag limit |
