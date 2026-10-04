# Examples: pragmatic-use-jobs

Copied from `examples`, which compiles in the repository and is exercised by
`examples/invoicing/tests/Invoicing.IntegrationTests` and `examples/warehouse/tests/Warehouse.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`invoicing/src/Invoicing.Billing/Infrastructure/Jobs/ChaseOverdueInvoicesJob.cs`](invoicing/src/Invoicing.Billing/Infrastructure/Jobs/ChaseOverdueInvoicesJob.cs) | A recurring job: `[RecurringJob]` with a cron and a time zone, `[Timeout]`, and one `TenantScope` per company; without it every read of a tenant-filtered module finds nothing |
| [`invoicing/src/Invoicing.Billing/BillingBoundary.cs`](invoicing/src/Invoicing.Billing/BillingBoundary.cs) | `[EnableJobPersistence]`: the job store's tables in the boundary's own database |
| [`invoicing/src/Invoicing.Host/Program.cs`](invoicing/src/Invoicing.Host/Program.cs) | `UseJobs`: worker count, polling interval, `UseEfCore()` and `UseEfCorePersistence()` |
| [`warehouse/src/Warehouse.Stock/Infrastructure/Jobs/ExpireReservationsJob.cs`](warehouse/src/Warehouse.Stock/Infrastructure/Jobs/ExpireReservationsJob.cs) | A parameterized job (`[Job]`, `IJob<T>`) that throws on failure so the store retries it |
| [`warehouse/src/Warehouse.Stock/Infrastructure/Jobs/ExpireReservations.cs`](warehouse/src/Warehouse.Stock/Infrastructure/Jobs/ExpireReservations.cs) | Its parameters: a record |
| [`warehouse/src/Warehouse.Stock/Reservations/Actions/ReserveStockAction.cs`](warehouse/src/Warehouse.Stock/Reservations/Actions/ReserveStockAction.cs) | Scheduling it with `IJobScheduler.ScheduleAtAsync` for the moment the holds expire, inside the `[Transactional]` action that holds them |
| [`invoicing/tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs`](invoicing/tests/Invoicing.IntegrationTests/ChasingOverdueInvoices.cs) | Testing a recurring job: its declared cron asserted, the work run on a day the test's clock chooses, the result read from the mailbox |
