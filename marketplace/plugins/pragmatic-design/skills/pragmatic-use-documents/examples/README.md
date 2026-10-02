# Examples — pragmatic-use-documents

Copied from `examples/invoicing/src/Invoicing.Billing`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`templates/invoice.pdxdoc`](templates/invoice.pdxdoc) | The invoice as a document template |
| [`templates/overdue-reminder.pdxemail`](templates/overdue-reminder.pdxemail) | The overdue reminder as a mail template |
| [`Infrastructure/Documents/Templates.cs`](Infrastructure/Documents/Templates.cs) | `[assembly: PdxTemplates<BillingModule>]`: the module declares its templates, the host registers them |
| [`Invoices/InvoiceDocument.cs`](Invoices/InvoiceDocument.cs) | Composing the invoice in the customer's language, and refusing a document with missing data (Warnings) |
| [`Infrastructure/Email/OverdueReminder.cs`](Infrastructure/Email/OverdueReminder.cs) | Composing the reminder mail and building the message with the issued PDF attached |
| [`translations/it-IT.json`](translations/it-IT.json) | The Italian translations the templates' t: keys read |
