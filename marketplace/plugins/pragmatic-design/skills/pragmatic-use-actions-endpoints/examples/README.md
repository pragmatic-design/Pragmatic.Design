# Examples: pragmatic-use-actions-endpoints

Copied from `examples/invoicing/src/Invoicing.Billing`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Invoices/Mutations/CreateDraftInvoiceMutation.cs`](Invoices/Mutations/CreateDraftInvoiceMutation.cs) | A create mutation on POST: permission, [CreatedAt], [ReturnsDto], and the lines it carries as child mutations |
| [`Invoices/Mutations/WriteInvoiceLineMutation.cs`](Invoices/Mutations/WriteInvoiceLineMutation.cs) | A child mutation: no [Endpoint], so a parent carries it and its rules travel with it |
| [`Invoices/Mutations/UpdateDraftInvoiceMutation.cs`](Invoices/Mutations/UpdateDraftInvoiceMutation.cs) | An update mutation on PUT that carries the same child lines |
| [`Invoices/Actions/IssueInvoiceAction.cs`](Invoices/Actions/IssueInvoiceAction.cs) | A domain action: [LoadEntity], [TransitionsTo], typed errors, and the PDF rendered through IPdxTemplates |
| [`Payments/Actions/RecordPaymentAction.cs`](Payments/Actions/RecordPaymentAction.cs) | A domain action with a conditional transition, validated money input, and a 409 for the wrong state |
| [`Invoices/Queries/ListInvoicesQuery.cs`](Invoices/Queries/ListInvoicesQuery.cs) | A paged list query with its endpoint and permission |
| [`Invoices/Queries/GetInvoiceQuery.cs`](Invoices/Queries/GetInvoiceQuery.cs) | A single-row query by id |
| [`Invoices/Endpoints/DownloadInvoiceEndpoint.cs`](Invoices/Endpoints/DownloadInvoiceEndpoint.cs) | An `Endpoint<FileResponse, NotFoundError>`: a route that returns a file |
| [`Invoices/Errors/InvoiceNotDraftError.cs`](Invoices/Errors/InvoiceNotDraftError.cs) | A domain error record and the status code it maps to (409) |
