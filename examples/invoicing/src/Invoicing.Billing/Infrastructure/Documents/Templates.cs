using Pragmatic.Documents.Markup;

// The invoice and the overdue reminder are templates embedded in this module: every host that includes
// it registers them as a template source, without a line of its own.
[assembly: PdxTemplates<Invoicing.Billing.BillingModule>]
