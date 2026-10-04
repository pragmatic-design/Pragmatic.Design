# Examples: pragmatic-use-audit

Copied from `examples`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests` and `examples/time-off/tests/TimeOff.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`invoicing/src/Invoicing.Billing/Invoices/Invoice.cs`](invoicing/src/Invoicing.Billing/Invoices/Invoice.cs) | An entity with both `[Auditable]` (who last touched the row) and `[Audited]` (an entry on the trail for every change) |
| [`invoicing/src/Invoicing.Billing/Infrastructure/Audit/BillingAuditOperations.cs`](invoicing/src/Invoicing.Billing/Infrastructure/Audit/BillingAuditOperations.cs) | The operation names an application writes to the trail: constants, never assembled at runtime, and additive because they are stored data |
| [`invoicing/src/Invoicing.Billing/Infrastructure/EventHandlers/RecordWhatTheMoveMeant.cs`](invoicing/src/Invoicing.Billing/Infrastructure/EventHandlers/RecordWhatTheMoveMeant.cs) | Writing an entry yourself with `IAuditTrail`: what an invoice's move meant (issued, paid, voided), from its domain events |
| [`time-off/src/TimeOff.Leave/Compliance/Actions/VerifyAuditTrailAction.cs`](time-off/src/TimeOff.Leave/Compliance/Actions/VerifyAuditTrailAction.cs) | Verifying the sealed segments of a period with `IAuditTrailReader.VerifyAsync`, behind a permission of its own |
| [`time-off/src/TimeOff.Leave/Compliance/Actions/ScanForSecurityIncidentsAction.cs`](time-off/src/TimeOff.Leave/Compliance/Actions/ScanForSecurityIncidentsAction.cs) | Raising incidents from trail patterns with `AuditPatternDetector`: a per-subject and a global rule on failed sign-ins |
