# Examples: pragmatic-use-multitenancy

Copied from `examples`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests` and `examples/casework/tests/Casework.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`invoicing/src/Invoicing.Billing/Invoices/Invoice.cs`](invoicing/src/Invoicing.Billing/Invoices/Invoice.cs) | `ITenantEntity` on an entity: the tenant written on create and filtered on read |
| [`invoicing/src/Invoicing.Host/Program.cs`](invoicing/src/Invoicing.Host/Program.cs) | `UseMultiTenancy` with the tenant from the token's claim and nowhere else, and the four guards written out |
| [`invoicing/src/Invoicing.Registry/Infrastructure/MultiTenancy/OrganizationTenantStore.cs`](invoicing/src/Invoicing.Registry/Infrastructure/MultiTenancy/OrganizationTenantStore.cs) | The register of tenants as an `ITenantStore` over the application's own table: what gives `RequireKnownTenant` and `EnforceTenantState` something to check |
| [`invoicing/src/Invoicing.Billing/Infrastructure/Jobs/ChaseOverdueInvoicesJob.cs`](invoicing/src/Invoicing.Billing/Infrastructure/Jobs/ChaseOverdueInvoicesJob.cs) | Work with no request: one `TenantScope` per active tenant, from the register |
| [`casework/src/Casework.Intake/Infrastructure/Jobs/ExpireOverdueVerifications.cs`](casework/src/Casework.Intake/Infrastructure/Jobs/ExpireOverdueVerifications.cs) | Reading across tenants with `FilterMode.Background`, then writing each tenant's rows in its own `TenantScope` and container scope |
| [`invoicing/tests/Invoicing.IntegrationTests/TheTenantComesFromTheToken.cs`](invoicing/tests/Invoicing.IntegrationTests/TheTenantComesFromTheToken.cs) | The refusals the guards make (no tenant 400, unknown 404, suspended 403), with an onboarded company as the control |
| [`casework/src/Casework.Intake.Host/Program.cs`](casework/src/Casework.Intake.Host/Program.cs) | A database per tenant beside the shared schema: `UseDbPerTenant`, the provisioner (`UseAutoProvision<PostgresTenantProvisioner>`), and the schema put into each new database |
