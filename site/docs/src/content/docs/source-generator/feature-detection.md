---
title: Feature Detection
description: How the generator detects which Pragmatic modules are referenced.
---

The unified generator doesn't have a configuration file. Instead it asks the Roslyn compilation: *"is this marker type reachable?"*. If yes, the corresponding **feature** is activated for this build. If no, the generator stays silent — no stale code, no useless files.

This is the **composition-by-presence** principle in action: add a NuGet, the generator notices; remove it, the generated code disappears.

## Mechanism

```csharp
// Pragmatic.SourceGenerator/Core/FeatureDetector.cs (abridged)
internal static class FeatureDetector
{
    public static DetectedFeatures Detect(Compilation compilation) => new()
    {
        HasActions = TypeExists(compilation, "Pragmatic.Actions.Attributes.DomainActionAttribute"),
        HasEndpoints = TypeExists(compilation, "Pragmatic.Endpoints.Attributes.EndpointAttribute"),
        HasPersistenceEFCore = TypeExists(compilation, "Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute"),
        HasMessaging = TypeExists(compilation, "Pragmatic.Messaging.Attributes.MessageHandlerAttribute"),
        HasJobs = TypeExists(compilation, "Pragmatic.Jobs.Attributes.RecurringJobAttribute"),
        HasResult = TypeExists(compilation, "Pragmatic.Result.IError"),
        // … 49 flags in all, plus IsHostMode and EfCoreProvider
    };

    private static bool TypeExists(Compilation compilation, string fullyQualifiedName)
        => compilation.GetTypeByMetadataName(fullyQualifiedName) is not null;
}
```

`Detect` runs once per compilation and returns an immutable `DetectedFeatures` record. The pipelines read the flags to decide what to emit. A marker is chosen as the type the generated code itself names — a probe for anything else could answer "yes" while the line the generator writes still would not compile. A few features (`FastEnum`, `Jobs`, `ValueObject`, the glossary documents) are registered unconditionally, because their attributes live in lightweight packages.

:::caution[Generic markers use backtick arity]
Generic attributes resolve under their CLR metadata name, so `MapFromAttribute<TSource>` is detected as `` Pragmatic.Mapping.Attributes.MapFromAttribute`1 `` — note the `` `1 `` suffix and the `.Attributes` sub-namespace. Dropping either makes the lookup miss.
:::

## The flags

The complete list, as [`FeatureDetector.cs`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Core/FeatureDetector.cs) probes it. The flags themselves are declared in [`DetectedFeatures.cs`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.SourceGenerator/src/Pragmatic.SourceGenerator/Core/DetectedFeatures.cs); a flag nothing reads has no probe, because every probe runs again on every compilation.

| Flag | Marker type (FQN) |
|------|-------------------|
| `HasActions` | `Pragmatic.Actions.Attributes.DomainActionAttribute` |
| `HasValidation` | `Pragmatic.Validation.Attributes.ValidationAttribute` |
| `HasCaching` | `Pragmatic.Caching.Attributes.CacheableAttribute` |
| `HasMapping` | `` Pragmatic.Mapping.Attributes.MapFromAttribute`1 `` |
| `HasEndpoints` | `Pragmatic.Endpoints.Attributes.EndpointAttribute` |
| `HasEndpointsOpenApi` | `Pragmatic.Endpoints.OpenApi.ApiDocumentationMapper` |
| `HasScalar` | `Scalar.AspNetCore.ScalarEndpointRouteBuilderExtensions` |
| `HasComposition` | (detected by `CompositionDetector`) |
| `HasPersistenceEFCore` | `Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute` |
| `HasEfCore` | `Microsoft.EntityFrameworkCore.DbContext` |
| `HasI18n` | `Pragmatic.Internationalization.Attributes.TranslationKeysAttribute` |
| `HasI18nAspNetCore` | `Pragmatic.Internationalization.AspNetCore.Extensions.I18NBuilder` |
| `HasPdxTemplates` | `Pragmatic.Documents.Markup.PdxTemplatesServiceCollectionExtensions` |
| `HasResult` | `Pragmatic.Result.IError` |
| `HasConfiguration` | `Pragmatic.Configuration.ConfigurationAttribute` |
| `HasResilience` | `Pragmatic.Resilience.Attributes.ResiliencePolicyAttribute` |
| `HasIdentityAspNetCore` | `Pragmatic.Identity.Authorization.PragmaticPermissionHandler` |
| `HasIdentityPersistence` | `Pragmatic.Identity.Persistence.Entities.RolePermission` |
| `HasAuthorization` | `Pragmatic.Authorization.PragmaticBuilderAuthorizationExtensions` |
| `HasMultiTenancy` | `Pragmatic.MultiTenancy.InMemoryTenantStore` |
| `HasFeatureFlags` | `Pragmatic.FeatureFlags.FeatureFlagServiceCollectionExtensions` |
| `HasDiscovery` | `Pragmatic.Discovery.Abstractions.IDiscoveryService` |
| `HasTemporal` | `Pragmatic.Temporal.Clock.SystemClock` |
| `HasTemporalJson` | `Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehaviorRegistry` |
| `HasTemporalAspNetCore` | `Pragmatic.Temporal.AspNetCore.Middleware.TemporalContextMiddleware` |
| `HasTemporalEfCore` | `Pragmatic.Temporal.EntityFrameworkCore.Conventions.TemporalPropertyRegistry` |
| `HasEvents` | `Pragmatic.Events.IDomainEventDispatcher` |
| `HasEventsEFCore` | `Pragmatic.Events.EFCore.LifecycleEventsInterceptor` |
| `HasMessaging` | `Pragmatic.Messaging.Attributes.MessageHandlerAttribute` |
| `HasMessagingEFCore` | `Pragmatic.Messaging.EFCore.EfCoreOutboxSource` |
| `HasMessagingChannels` | `Pragmatic.Messaging.Channels.ChannelTransport` |
| `HasMessagingRabbitMq` | `Pragmatic.Messaging.RabbitMQ.RabbitMqTransport` |
| `HasMessagingBatch` | `Pragmatic.Messaging.Batch.IBatchProgressStore` |
| `HasJobs` | `Pragmatic.Jobs.Attributes.RecurringJobAttribute` |
| `HasJobsEFCore` | `Pragmatic.Jobs.EFCore.Entities.JobEntityTypeConfiguration` |
| `HasMigrations` | `Pragmatic.Migrations.Schema.SchemaVersion` |
| `HasNpgsqlDriver` / `HasSqlServerDriver` / `HasSqliteDriver` | `Npgsql.NpgsqlConnection` / `Microsoft.Data.SqlClient.SqlConnection` / `Microsoft.Data.Sqlite.SqliteConnection` |
| `HasPrivacy` | `Pragmatic.Privacy.PersonalDataAttribute` |
| `HasPrivacyRuntime` | `Pragmatic.Privacy.IPersonalDataSource` |
| `HasPrivacyEFCore` | `Pragmatic.Privacy.EFCore.PrivacyDbContext` |
| `HasAudit` | `Pragmatic.Audit.IAuditTrail` |
| `HasAuditEFCore` | `Pragmatic.Audit.EFCore.AuditDbContext` |
| `HasCryptographyEFCore` | `Pragmatic.Cryptography.EFCore.ProtectedValueConverter` |
| `HasImaging` | `Pragmatic.Imaging.ImagePipeline` |
| `HasNotifications` | `Pragmatic.Notifications.INotificationService` |
| `HasNotificationsEFCore` | `Pragmatic.Notifications.EFCore.NotificationDbContext` |
| `HasSerialization` | `Pragmatic.Serialization.PragmaticJsonOptions` |
| `IsHostMode` | (detected by `CompositionDetector`: an entry point plus `Composition.Host`) |

## EF Core provider detection

Beyond the boolean flags, `FeatureDetector` resolves an `EfCoreProvider` enum so the persistence templates can emit provider-specific SQL. It probes the referenced EF Core provider assemblies in priority order:

| Probe (marker type) | Result |
|---------------------|--------|
| `Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions` | `PostgreSql` |
| `Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsBuilderExtensions` | `SqlServer` |
| `Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions` | `Sqlite` |
| *(none of the above)* | `Generic` |

PostgreSQL wins over SQL Server, which wins over SQLite — so a project referencing more than one provider resolves to the highest-priority match.

## Why a marker type instead of assembly name?

Three reasons, in order of importance:

1. **Meta-packages don't break detection.** If a `Pragmatic.Composition` meta-package pulls in `Pragmatic.Composition.Host` plus `Pragmatic.Actions`, detection by assembly name would produce duplicate triggers. Detection by **type** fires exactly once per real type, independent of how you got there.
2. **Renames are decoupled.** If a module is ever renamed at the assembly level, the type-level marker can stay stable — detection keeps working until the real API moves.
3. **Fast at compile time.** `GetTypeByMetadataName` is one of the cheapest Roslyn lookups. No reflection walk, no assembly enumeration.

## Cross-feature composition

Detection is not binary-only: when multiple features are active, they can compose. Example:

- `HasActions` **and** `HasPersistenceEFCore` → the Action template can emit `LoadEntity` invocations that go through the generated repository.
- `HasMessaging` **and** `HasPersistenceEFCore` → the Messaging template emits an outbox that uses the `DbContext` from the persistence feature.
- `HasAuthorization` **and** `HasActions` → the Action invoker gets a permission check filter automatically.

These cross-feature enrichers live in the top-level `Compositions/` folder of the generator and run after the per-feature transforms.

## Adding a detection probe

When you add a new feature:

```csharp
// Core/DetectedFeatures.cs
public bool HasMyFeature { get; init; }
```

```csharp
// Core/FeatureDetector.cs — inside the Detect(...) object initializer
HasMyFeature = TypeExists(compilation, "Pragmatic.MyFeature.SomeMarkerType"),
```

The rest of the generator pipeline can then guard its work on `detected.HasMyFeature`. Tests cover the matrix of "feature absent" vs "feature present" to ensure the generator produces zero output when the module is not referenced.

## Debugging detection

If a feature is unexpectedly silent:

1. With `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`, open `obj/Debug/net10.0/generated/` and check whether any `_Infra.*.g.cs` was emitted for that feature. If missing, the flag was false.
2. Verify the project references the module's package, directly or transitively — check the graph with `dotnet list package --include-transitive`.
3. Compare with the marker in the table above: the probe asks for that exact type.
