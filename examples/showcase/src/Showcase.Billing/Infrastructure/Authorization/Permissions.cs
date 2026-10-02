// L2 custom permission: refunding goes past the Invoice CRUD. Its constant, BillingPermissions.Invoice.Refund,
// sits beside the CRUD ones; its description and category are what a role screen lists.
[assembly: Permission("billing.invoice.refund", "Refund a paid invoice", Category = "Billing")]

// The gate on the read that crosses the data scopes. ⚠️ Not the generated billing.invoice.view-all:
// that one is the CALLER's authority, held by whoever is allowed to see every row anywhere. This one
// is the AUDIT's — the operation reads across scopes whoever calls it, and holding this is permission
// to run that operation, not permission to see everything through every other route.
[assembly: Permission("billing.invoice.audit", "Read every invoice of the tenant, across data scopes",
    Category = "Billing")]
