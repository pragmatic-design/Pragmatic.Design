# Examples: pragmatic-use-logging

Copied from `examples`, which compiles in the repository and is exercised by
`examples/time-off/tests/TimeOff.IntegrationTests`, `examples/invoicing/tests/Invoicing.IntegrationTests` and `examples/showcase/tests/Showcase.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`showcase/src/Showcase.Host/Program.cs`](showcase/src/Showcase.Host/Program.cs) | `UseLogging`: the development console and a daily rolling file, among the host's other strategy calls |
| [`invoicing/src/Invoicing.Billing/Infrastructure/Jobs/ChaseOverdueInvoicesJob.cs`](invoicing/src/Invoicing.Billing/Infrastructure/Jobs/ChaseOverdueInvoicesJob.cs) | `[LoggerMessage]` on a partial class: structured lines that name identifiers, not values |
| [`time-off/src/TimeOff.Leave/Employees/Actions/ProvisionFirstAdministratorAction.cs`](time-off/src/TimeOff.Leave/Employees/Actions/ProvisionFirstAdministratorAction.cs) | `[NotLogged]` on an input that is a secret with no data subject |
| [`time-off/tests/TimeOff.IntegrationTests/WhatTheLogsMayNotSay.cs`](time-off/tests/TimeOff.IntegrationTests/WhatTheLogsMayNotSay.cs) | Asserting declared redaction through the application's own logger factory: classified members masked and still named, secrets gone, an unclassified field as the control |
