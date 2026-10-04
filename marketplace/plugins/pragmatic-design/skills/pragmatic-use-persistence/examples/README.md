# Examples: pragmatic-use-persistence

Copied from `examples/invoicing/src`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Invoicing.Billing/Invoices/Invoice.cs`](Invoicing.Billing/Invoices/Invoice.cs) | An aggregate: `[Entity]`, `[ConcurrencyAware]`, `[StateMachine<T>]`, relations with their delete behaviour, a `[LogicKey]`, money columns, a `[ComputedFilter]` method, and another boundary's row kept as an id |
| [`Invoicing.Billing/Invoices/InvoiceLine.cs`](Invoicing.Billing/Invoices/InvoiceLine.cs) | A child: `[PartOf<Invoice>]`, validation on its columns, and an `[Invariant]` a constant attribute could not express |
| [`Invoicing.Billing/Enums/InvoiceStatus.cs`](Invoicing.Billing/Enums/InvoiceStatus.cs) | The state machine: `[InitialState]` and `[TransitionFrom]`, and the generated `TransitionTo` refuses every other move |
| [`Invoicing.Registry/Customers/Customer.cs`](Invoicing.Registry/Customers/Customer.cs) | `[SoftDelete]`, `[Unique]` within the tenant, and a `[GeneratedValue]` code from a database sequence |
| [`Invoicing.Billing/Invoices/Queries/ListInvoicesQuery.cs`](Invoicing.Billing/Invoices/Queries/ListInvoicesQuery.cs) | A declarative paged query: `[Filter]` with operators, `[SearchAcross]`, `[Sort]`, and a `[BindSpecification]` fed by `[FromClock]` |
| [`Invoicing.Billing/Invoices/Queries/OutstandingByCustomerQuery.cs`](Invoicing.Billing/Invoices/Queries/OutstandingByCustomerQuery.cs) | An aggregate query, narrowed by specifications the caller cannot switch off |
| [`Invoicing.Billing/Invoices/Queries/OutstandingByCustomerLine.cs`](Invoicing.Billing/Invoices/Queries/OutstandingByCustomerLine.cs) | Its view: `[QueryView<T>]`, `[GroupBy<T>]`, and `[Count]`/`[Sum]`/`[Min]` computed by the database |
| [`Invoicing.Billing/Invoices/InvoiceSpecifications.cs`](Invoicing.Billing/Invoices/InvoiceSpecifications.cs) | Specifications in the generated container, composing the entity's own computed filter |
