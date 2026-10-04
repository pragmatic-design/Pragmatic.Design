# Examples: pragmatic-use-migrations

Copied from `examples`, which compiles in the repository and is exercised by
`examples/warehouse/tests/Warehouse.IntegrationTests` and `examples/showcase/tests/Showcase.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`warehouse/src/Warehouse.Shipping.Host/Program.cs`](warehouse/src/Warehouse.Shipping.Host/Program.cs) | `UsePragmaticMigrations()`: the host's database migrated at start, before it serves |
| [`warehouse/src/Warehouse.Shipping.Host/ShippingDatabase.cs`](warehouse/src/Warehouse.Shipping.Host/ShippingDatabase.cs) | The database it migrates: `[PragmaticDatabase]` with its provider and connection-string key |
| [`warehouse/src/Warehouse.Shipping.Host/Warehouse.Shipping.Host.csproj`](warehouse/src/Warehouse.Shipping.Host/Warehouse.Shipping.Host.csproj) | `<PragmaticSchemaSnapshot>` and the migrations targets: the schema written at build time |
| [`warehouse/src/Warehouse.Shipping.Host/schema/ShippingDatabase.schema.json`](warehouse/src/Warehouse.Shipping.Host/schema/ShippingDatabase.schema.json) | The committed snapshot (tables, columns, indexes, the outbox's table included), so a schema change shows in the diff of the commit that caused it |
| [`showcase/tests/Showcase.IntegrationTests/Migrations/MigrationRunnerE2ETests.cs`](showcase/tests/Showcase.IntegrationTests/Migrations/MigrationRunnerE2ETests.cs) | The runner against a real PostgreSQL: a breaking change refused in one transaction with its suggestions, an `IDataMigration` run exactly once, `ManageDeclaredTablesOnly()` leaving a foreign table alone |
