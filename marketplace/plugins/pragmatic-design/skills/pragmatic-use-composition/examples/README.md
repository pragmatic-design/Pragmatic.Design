# Examples — pragmatic-use-composition

Copied from `examples/invoicing/src`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`Invoicing.Host/Program.cs`](Invoicing.Host/Program.cs) | The host: `PragmaticApp.RunAsync` and only the strategy calls — languages, storage, jobs, mail, migrations, OIDC, tenancy, roles — while the registrations are generated |
| [`Invoicing.Host/InvoicingHostModule.cs`](Invoicing.Host/InvoicingHostModule.cs) | The topology: `[Include<TModule, TDatabase>]` for each module, and the steps the host needs (`[NeedsStep<T>]`) |
| [`Invoicing.Host/AppDatabase.cs`](Invoicing.Host/AppDatabase.cs) | The database marker: `[PragmaticDatabase]` with its provider and connection-string key |
| [`Invoicing.Billing/BillingModule.cs`](Invoicing.Billing/BillingModule.cs) | A module that builds on another: `[IncludeModule<RegistryModule>]` |
| [`Invoicing.Billing/BillingBoundary.cs`](Invoicing.Billing/BillingBoundary.cs) | A `[Boundary]`, asking for the job store's tables in its own database (`[EnableJobPersistence]`) |
| [`Invoicing.Registry/Infrastructure/MultiTenancy/OrganizationTenantStore.cs`](Invoicing.Registry/Infrastructure/MultiTenancy/OrganizationTenantStore.cs) | `[Service<ITenantStore>]` replacing the framework's empty default with a read view over the application's own table |
| [`Invoicing.Billing/Infrastructure/Jobs/OverdueReminderSweep.cs`](Invoicing.Billing/Infrastructure/Jobs/OverdueReminderSweep.cs) | `[Service<T>]` on a class whose dependencies are registered elsewhere — by another module and by the host's `UseStorage`/`UseEmail` |
