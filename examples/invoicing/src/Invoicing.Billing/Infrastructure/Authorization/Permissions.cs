// The permissions of this boundary that are not an entity's CRUD. Each becomes a constant of
// BillingPermissions, beside the generated CRUD ones, and an entry of the permission catalogue.

// Issuing a draft: it takes the issuer's next number, freezes the customer and becomes a document. Not the
// invoice's update — after this the invoice cannot be changed, and that is a different thing to be allowed.
[assembly: Permission("billing.invoice.issue", "Issue a draft invoice: number it and render its document", Category = "Billing")]

// Voiding an issued invoice. Kept apart from delete: a void invoice stays, numbered, and says it is void.
[assembly: Permission("billing.invoice.void", "Void an issued invoice", Category = "Billing")]

// Downloading the PDF of an issued invoice.
[assembly: Permission("billing.invoice.download", "Download the document of an issued invoice", Category = "Billing")]

// Recording what a customer paid. A permission of its own: whoever writes invoices is not necessarily
// whoever is trusted to say money arrived.
[assembly: Permission("billing.payment.record", "Record and remove the payments of an invoice", Category = "Billing")]
