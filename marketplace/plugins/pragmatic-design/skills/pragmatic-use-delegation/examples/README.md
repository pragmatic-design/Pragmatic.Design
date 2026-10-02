# Examples — pragmatic-use-delegation

Copied from `examples/showcase`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`src/Showcase.Booking/Guests/Actions/RegisterGuestOnBehalfOfAction.cs`](src/Showcase.Booking/Guests/Actions/RegisterGuestOnBehalfOfAction.cs) | `[StartsDelegation]` on a domain action: the permission is checked against the caller, then the whole invocation — the owner stamped at save included — runs as the subject |
| [`tests/Showcase.IntegrationTests/Authorization/WhoTheRowBelongsToWhenSomebodyElseTypedItTests.cs`](tests/Showcase.IntegrationTests/Authorization/WhoTheRowBelongsToWhenSomebodyElseTypedItTests.cs) | What that means on the row: written under delegation it belongs to the subject, readable by them and not by the caller who typed it — with the ordinary write as the control |
