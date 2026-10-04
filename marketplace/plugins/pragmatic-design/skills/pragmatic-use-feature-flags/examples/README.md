# Examples: pragmatic-use-feature-flags

Copied from `examples`, which compiles in the repository and is exercised by
`examples/showcase/tests/Showcase.IntegrationTests` and `examples/warehouse/tests/Warehouse.IntegrationTests`. **Do not edit here**: change the source and run
`node scripts/sync-skill-examples.mjs`. The gate refuses a copy that differs from its source.

| File | What it shows |
|---|---|
| [`warehouse/src/Warehouse.Stock/Infrastructure/FeatureFlags/AcceptBackordersFlag.cs`](warehouse/src/Warehouse.Stock/Infrastructure/FeatureFlags/AcceptBackordersFlag.cs) | A strongly-typed flag: `IFeatureFlag` with its name and description |
| [`warehouse/src/Warehouse.Stock/Reservations/Actions/ReserveStockAction.cs`](warehouse/src/Warehouse.Stock/Reservations/Actions/ReserveStockAction.cs) | Read at each request with `IFeatureFlags.IsEnabledAsync<T>`, so a switch through the Agent reaches every instance without a restart |
| [`showcase/src/Showcase.Host/FeatureFlags/ShowcaseFeatureFlagSeeder.cs`](showcase/src/Showcase.Host/FeatureFlags/ShowcaseFeatureFlagSeeder.cs) | Defining flags in the in-memory store from their types, with the `FeatureFlagRule` factories: percentage, tenant, plan |
| [`showcase/src/Showcase.Host/FeatureFlags/ShowcaseFeatureFlagContextProvider.cs`](showcase/src/Showcase.Host/FeatureFlags/ShowcaseFeatureFlagContextProvider.cs) | The `IFeatureFlagContextProvider` the application registers: tenant, user, a plan claim, the environment |
| [`showcase/src/Showcase.Booking/Reservations/Mutations/CheckInGuestMutation.cs`](showcase/src/Showcase.Booking/Reservations/Mutations/CheckInGuestMutation.cs) | Asking with the ambient context: `IFeatureFlags.IsEnabledAsync<EarlyCheckInFlag>` |
| [`showcase/src/Showcase.Booking/Reservations/Actions/CreateReservationAction.cs`](showcase/src/Showcase.Booking/Reservations/Actions/CreateReservationAction.cs) | Asking about somebody else: `IFeatureFlagStore` with an explicit context, the guest and not the caller |
| [`showcase/src/Showcase.Host.Distributed/Program.cs`](showcase/src/Showcase.Host.Distributed/Program.cs) | Flags from configuration: `AddConfigurationFeatureFlagStore()` |
