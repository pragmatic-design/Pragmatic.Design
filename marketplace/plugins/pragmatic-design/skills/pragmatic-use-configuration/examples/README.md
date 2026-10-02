# Examples — pragmatic-use-configuration

Copied from `examples`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests` and `examples/warehouse/tests/Warehouse.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`showcase/src/Showcase.Host/Configuration/ShowcaseOptions.cs`](showcase/src/Showcase.Host/Configuration/ShowcaseOptions.cs) | `[Configuration]` on an options class: DataAnnotations checked at start, `[Sensitive]` keeping a value out of the change history, `[ConfigInvariant]` for a rule between two settings |
| [`warehouse/src/Warehouse.Stock/Infrastructure/Configuration/ReorderOptions.cs`](warehouse/src/Warehouse.Stock/Infrastructure/Configuration/ReorderOptions.cs) | `[Configuration(SectionPath = …)]` for a setting that changes at runtime |
| [`warehouse/src/Warehouse.Stock/Products/Actions/ListProductsToReorderAction.cs`](warehouse/src/Warehouse.Stock/Products/Actions/ListProductsToReorderAction.cs) | Reading it through `IOptionsMonitor<T>.CurrentValue`, so a change reaches every instance without a restart |
| [`showcase/src/Showcase.Accounts/AccountsModule.cs`](showcase/src/Showcase.Accounts/AccountsModule.cs) | The management package: `[UsePackage<ConfigurationManagementPackage, …>]`, with only the read exposed as a route (`[ExposeEndpoint<GetConfigValues, …>]`) |
