# Examples: pragmatic-use-resilience

Copied from `examples/showcase`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests` and `examples/showcase/tests/Showcase.Tests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`src/Showcase.Billing/Invoices/Actions/RefundInvoiceAction.cs`](src/Showcase.Billing/Invoices/Actions/RefundInvoiceAction.cs) | `[ResiliencePolicy("payment-provider")]` on an action that calls an external provider before it changes the invoice |
| [`src/Showcase.Billing/Infrastructure/EventHandlers/InvoicePaidHandler.cs`](src/Showcase.Billing/Infrastructure/EventHandlers/InvoicePaidHandler.cs) | `[Retry]` and `[CircuitBreaker]` on a message handler whose dependency is an HTTP call in the distributed topology, and why a failure counts per attempt |
| [`tests/Showcase.Tests/Unit/EventHandlers/WhenBookingIsDownTheHandlerStopsTryingTests.cs`](tests/Showcase.Tests/Unit/EventHandlers/WhenBookingIsDownTheHandlerStopsTryingTests.cs) | Testing the breaker: the assertion is that the handler stops being entered, not that it fails |
