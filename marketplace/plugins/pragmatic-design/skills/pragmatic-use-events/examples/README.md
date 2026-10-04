# Examples: pragmatic-use-events

Copied from `examples/invoicing/src`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Invoicing.Billing/Events/InvoiceIssued.cs`](Invoicing.Billing/Events/InvoiceIssued.cs) | A domain event: a record deriving from `DomainEvent` |
| [`Invoicing.Billing/Enums/InvoiceStatus.cs`](Invoicing.Billing/Enums/InvoiceStatus.cs) | `[RaisesEvent<T>]` on the state machine's target states: the move raises the event, whichever operation takes it |
| [`Invoicing.Registry/Customers/Mutations/CreateCustomerMutation.cs`](Invoicing.Registry/Customers/Mutations/CreateCustomerMutation.cs) | `[Raises<T>]` on a create mutation |
| [`Invoicing.Registry/Events/CustomerRegistered.cs`](Invoicing.Registry/Events/CustomerRegistered.cs) | An event built after the save, so it carries the code the database sequence gave the row |
| [`Invoicing.Billing/Infrastructure/EventHandlers/RecordWhatTheMoveMeant.cs`](Invoicing.Billing/Infrastructure/EventHandlers/RecordWhatTheMoveMeant.cs) | One `[EventHandler]` class handling three events |
| [`Invoicing.Registry/Infrastructure/EventHandlers/RecordTheCodeACustomerWasGiven.cs`](Invoicing.Registry/Infrastructure/EventHandlers/RecordTheCodeACustomerWasGiven.cs) | A handler that takes the generated value from the event instead of reading the row back |
