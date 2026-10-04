# Examples: pragmatic-use-email

Copied from `examples/invoicing`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`src/Invoicing.Billing/Infrastructure/Email/OverdueReminder.cs`](src/Invoicing.Billing/Infrastructure/Email/OverdueReminder.cs) | Building the message with `EmailMessageBuilder`: the template's subject, HTML and text, the company's own sender, the stored PDF attached |
| [`src/Invoicing.Billing/Infrastructure/Jobs/OverdueReminderSweep.cs`](src/Invoicing.Billing/Infrastructure/Jobs/OverdueReminderSweep.cs) | Sending it with `IEmailSender`, and recording the reminder only once the transport accepted it |
| [`src/Invoicing.Host/Program.cs`](src/Invoicing.Host/Program.cs) | `UseEmail`: SMTP from configuration, the null transport without it, and no `DefaultFrom` on purpose |
| [`tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs`](tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs) | Testing with `AddEmailTestHarness()`: one reminder per invoice, from the company's address, with the issued PDF attached byte for byte |
