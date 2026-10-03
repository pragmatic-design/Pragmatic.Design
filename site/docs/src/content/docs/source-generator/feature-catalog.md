---
title: Feature Catalog
description: Every generator pipeline: its trigger, what it generates, and a short input→output sketch.
---

This is the catalog of code-generating pipelines in Pragmatic Design: the **feature pipelines** inside the unified `Pragmatic.SourceGenerator` (31 feature folders, including four documentation generators), the inline **Manifest** generator, and the **standalone** generators that ship with their own modules.

Each entry lists the **trigger** (the attribute or marker that activates it) and a short sketch of the generated output. Triggers are quoted from `FeatureDetector.cs` / `AttributeNames.cs`; where the exact output is large, it is summarised rather than reproduced.

:::note
Generation is gated on detection: a pipeline produces **zero output** unless its module is referenced. `FastEnum`, `Jobs` and `ValueObject` are the exceptions: they are registered unconditionally because their attributes live in lightweight packages. See [feature detection](/source-generator/feature-detection/).
:::

## Unified generator: feature pipelines

### Actions
**Trigger:** `[DomainAction]` and `[Mutation]` (`Pragmatic.Actions.Attributes.*`) on a `partial` class.
**Generates:** a typed *invoker* (load → authorization → validation → execute → commit → events) nested in the operation's partial class, a `SetDependencies` injection point, the boundary interface that composes the operations, and centralized DI registration.

```csharp
[DomainAction]
public partial class PlaceOrder : DomainAction<OrderResult>
{
    public override Task<Result<OrderResult, IError>> Execute(CancellationToken ct = default) => …;
}
// → partial class PlaceOrder { … Invoker … }  + the boundary interface + DI registration
```

### Endpoints
**Trigger:** `[Endpoint]` / `[ExposeEndpoint<T>]` / `[Get]` `[Post]` `[Put]` `[Delete]` (`Pragmatic.Endpoints.Attributes.*`).
**Generates:** minimal-API endpoint registrations bound to the matching action/query, with route, verb, and parameter binding. Also invokes the **Manifest** generator inline.

### Persistence (orchestrator → 6 sub-features)
**Trigger:** `[Query<TEntity, TResult>]` (runtime `HasPersistence`) and `[PragmaticDbContext]` (`HasPersistenceEFCore`).
`PersistenceFeature` is a thin orchestrator delegating to six sub-features:

| Sub-feature | Generates |
|-------------|-----------|
| **EntityCore** | entity trait properties (`PersistenceId`, `Id`, audit, soft-delete), `Create(...)` factories, relation setters |
| **Repository** | per-entity repositories, query filters, metadata |
| **Query** | `Query`/`GridFilter`/`FilterDto` + `Apply()` projection bodies |
| **Projection** | `[Projectable]`, computed filters, DTO include expressions |
| **Advanced** | inheritance, hierarchy, timeline, lookup |
| **DbContext** | `BoundaryDbContext`, `MigrationDbContext`, host-level `EntityConfig.*` |

Provider-specific SQL is selected via the detected `EfCoreProvider` (PostgreSQL / SQL Server / SQLite / Generic).

### Validation
**Trigger:** `[Validation]` / `[Validator]` (`Pragmatic.Validation.Attributes.*`).
**Generates:** compiled validators and the validation pass invoked by the action invoker, with no expression-tree or reflection cost at runtime.

### Mapping
**Trigger:** `` [MapFrom<TSource>] `` / `` [MapTo<T>] `` (`` Pragmatic.Mapping.Attributes.MapFromAttribute`1 ``).
**Generates:** `FromEntity` / `ToEntity` mapping methods on the annotated DTO partial; `[MapIgnore]` excludes members.

```csharp
[MapFrom<User>]
public partial class UserDto { public string Name { get; set; } }
// → partial class UserDto { public static UserDto FromEntity(User e) => ...; }
```

### Caching
**Trigger:** `[Cacheable]` / `[CacheKey]` / `[InvalidatesCache]` (`Pragmatic.Caching.Attributes.*`).
**Generates:** caching decorators around actions/queries, deterministic cache-key builders, and invalidation hooks.

### Patch
**Trigger:** `` [GeneratePatch<T>] `` (`` Pragmatic.Patch.Attributes.GeneratePatchAttribute`1 ``).
**Generates:** a patch DTO of `Optional<T>` fields plus an `ApplyTo(target)` method that only writes the members that were explicitly set (JSON-merge / PATCH semantics).

### Composition
**Trigger:** Composition assembly reference (detected by `CompositionDetector`); host mode adds an executable entry point + `Pragmatic.Composition.Host` reference.
**Generates (host mode):** the aggregated `PragmaticHost.Services.g.cs` DI wiring, endpoint route groups, and `RemoteBoundary` HTTP invokers/dispatcher for distributed boundaries. This is where most cross-feature aggregation lands. Emits the bulk of the `PRAG16xx` diagnostics.

### Configuration
**Trigger:** `[Configuration]` (`Pragmatic.Configuration.ConfigurationAttribute`).
**Generates:** strongly-typed options binding + registration for a configuration section, with validation diagnostics (`PRAG2000`, `PRAG2001`, `PRAG2050`).

### Identity
**Trigger:** `[PragmaticUser]` / `[ProfileProperty]` (`Pragmatic.Identity.*`); persistence variant via `HasIdentityPersistence`.
**Generates:** `ToProfile()` + profile record, claim→entity user resolvers, and the permission registry from `[assembly: Permission]` and `IRole`. The constants themselves are the one `{Boundary}Permissions` class, beside the entity CRUD.

### Messaging
**Trigger:** `[MessageHandler]` (`Pragmatic.Messaging.Attributes.MessageHandlerAttribute`) and related saga/outbox attributes.
**Generates:** handler pipelines (with `[Retry]`/`[Timeout]` middleware), message-type registry, outbox source, topology + routing registries, and saga orchestrators. Diagnostics `PRAG0800-0831`.

### I18n
**Trigger:** `[assembly: TranslationKeys]` (`Pragmatic.Internationalization.Attributes.TranslationKeysAttribute`) + `.json` translation `AdditionalFiles`.
**Generates:** strongly-typed translation-key accessors from the resource files, validated against the configured keys (`PRAG1800-1803`).

### Result (feature pipeline)
**Trigger:** `Pragmatic.Result.IError` present (`HasResult`).
**Within the unified generator** this drives Result-aware composition in other features. The actual `Result`/`VoidResult` type *variants* are emitted by the **standalone** `ResultSourceGenerator` (below).

### Resource
**Trigger:** `[Resource]` (`Pragmatic.Persistence.Entity.ResourceAttribute`) on an entity class.
**Generates:** from a kebab-case route segment + `Capabilities` flags (`Create`/`Read`/`Update`/`Delete`/`List`/`Search`), the full CRUD surface: Create/Read/Update/Delete actions, List/Search query classes, the matching DTOs, and REST endpoints. The action/query/endpoint models are injected into the Actions/Query/Endpoints pipelines rather than emitted standalone. Diagnostics `PRAG2602-2605`.

### Traits
**Trigger:** `[HasComments]`, `[HasTags]`, `[HasNotes]`, `[HasAttachments]` (`Pragmatic.Comments` / `Pragmatic.Tags` / `Pragmatic.Notes` / `Pragmatic.Attachments`) on an entity.
**Generates:** a complete sub-feature per trait: the child entity (e.g. `OrderComment`), its EF `EntityConfig`, the parent navigation property, CRUD action classes, permission constants, a DTO, a paged list query, and (when a `[Resource]` is present) REST endpoints. Tags additionally generate a junction entity. Requires `HasPersistenceEFCore`; actions/endpoints require `HasActions`. Diagnostics `PRAG2600-2601`.

```csharp
[Resource("comments")]
[HasComments]
public partial class Article { ... }
// → ArticleComment entity + config + nav, Add/GetById/Update/Delete actions,
//    ArticleCommentDto, ListArticleCommentsQuery, permissions, endpoints
```

### FastEnum *(registered unconditionally)*
**Trigger:** `[FastEnum]` (`Pragmatic.FastEnumAttribute`) on an `enum`.
**Generates:** a `{Enum}Extensions` static class with zero-reflection helpers: `ToStringFast()`, `IsDefined(value)` / `IsDefined(string)`, `TryParse(...)` (+ case-insensitive overload), `GetValues()`, `GetNames()`, `Count`. It also generates a separate AOT-safe `{Enum}JsonConverter` (serializes as the string name). Optional `GetDisplayName()` (from `[Display]`/`[Description]`) and, when I18n is referenced, `GetI18nKey()` / `GetLocalizedName()`.

```csharp
[FastEnum] public enum Status { Active, Archived }
// → public static class StatusExtensions { ToStringFast, IsDefined, TryParse, GetValues, GetNames, Count }
//   public sealed class StatusJsonConverter : JsonConverter<Status>
```

### Jobs *(registered unconditionally)*
**Trigger:** `[Job]` / `[RecurringJob]` (+ `[Retry]`, `[Timeout]`, `` [ContinueWith<T>] ``) on a `partial` `IJob`/`IJob<T>`.
**Generates:** a typed job invoker, a job-type registry, recurring-job registration (cron-validated), and DI/metadata. Diagnostics `PRAG2500-2506`.

### ValueObject *(registered unconditionally)*
**Trigger:** `[ValueObject]` (`Pragmatic.Persistence.Entity.ValueObjectAttribute`) on a `partial record`.
**Generates:** factory methods into the record: `Create(...)` (calls the user's `Validate(...)`, returning the value object or a validation error) when a `Validate` method exists, and `CreateUnsafe(...)` (direct construction, for deserialization / trusted paths) when a constructor exists. Neither is emitted if the user already declared it. Diagnostics `PRAG2700-2702`.

```csharp
[ValueObject]
public partial record Email(string Value)
{
    public static Result<Email, ValidationError> Validate(string value) => ...;
}
// → public static Result<Email, ValidationError> Create(string value) => Validate(value);
//   public static Email CreateUnsafe(string value) => new Email(value);
```

### Documentation generators *(registered unconditionally)*
Four pipelines (in `Features/Glossary/`) emit living documentation as compile-time constants; surface them however you like (an endpoint, a build step), the same way the generated OpenAPI is served.

| Generator | Trigger | Output |
|-----------|---------|--------|
| **Glossary** | `[Entity]` types with XML-doc summaries | `PragmaticGlossary.Markdown`: ubiquitous-language glossary grouped by namespace |
| **Architecture (C4)** | `[Include<TModule>]` / `[RemoteBoundary<TModule>]` on the host | `PragmaticArchitecture.C4ContainerDiagram`: a Mermaid container diagram (remote boundaries marked) |
| **AsyncAPI** | types implementing `IDomainEvent` | `PragmaticAsyncApi.Json`: an AsyncAPI 3.0 document; one channel + message per event, with the payload schema |
| **Use cases** | `[UseCase]` / `[Rule]` on an operation | `PragmaticUseCases.All`: typed `UseCaseDescriptor`s (id, title, rules, target, file, line), plus `.Markdown` |

The AsyncAPI document is the **event contract**: each message carries its payload properties (so it is snapshot-testable) and is tagged `x-pragmatic-public` (the two-level model: `IIntegrationEvent` / `[PublicEvent]`) and `x-pragmatic-obsolete` (`[ObsoleteEvent]`).

### Smaller pipelines

Each of these does one thing, usually publishing a fact about the assembly so the host can compose it:

| Pipeline | What it does |
|----------|--------------|
| **Migrations** | Tells the migrations runtime which database drivers the application can actually use (a module initializer) |
| **Redaction** | Emits the assembly's `IRedactionMap` from the members marked `[NotLogged]` and `[PersonalData]`, and registers it |
| **Resilience** / **FeatureFlags** | Publish whether an assembly *declares* a policy or a flag, so the host wires the capability because somebody asked for it |
| **Documents** | Publishes that an assembly embeds `.pdxdoc` / `.pdxemail` templates, so the host registers it as a template source |
| **Read contracts** | An `I{Module}Reads` interface, implementation and registration per boundary, so another module can check a cross-boundary invariant |
| **Roll-ups** | The rules behind `[RollUp]`, published so the host's interceptor has them |
| **Serialization** | A per-assembly `JsonSerializerContext` for the boundary types, opt-in with `PragmaticGenerateJsonContext` (or `PublishAot`) |
| **Temporal** | The registration of the time-zone conversion attributes on DTO properties |
| **Privacy** | The graph of entities reachable from a data subject, with diagnostics for what is missing or contradictory |
| **Lifecycle events** | The `IRaisesLifecycleEvents` partial behind `[Raises<TEvent>]` |
| **Specification** | Makes a declared `Specification<TEntity>` usable on `IQueryable<TEntity>` and `IReadRepository<TEntity>` |
| **Local identity store** | An `ILocalIdentityStore` over the user entity, unless the application writes its own |

### Manifest *(inline, via EndpointsFeature)*
**Trigger:** runs from `EndpointsFeature` when endpoint models exist (not a standalone `[Generator]`).
**Generates:** a unified compile-time API manifest (JSON) aggregating endpoint + DTO metadata. Feeds OpenAPI enrichment, CLI client generation, and the WASM client generator. Generation failure surfaces as `PRAG9000` (Error) on that output alone; the other generated outputs are unaffected.

## Standalone generators

These are **separate `[Generator]` assemblies**, not feature pipelines inside the unified generator.

### Result: `ResultSourceGenerator`
*Assembly:* `Pragmatic.Result.SourceGenerator`.

Runs only while compiling the `Pragmatic.Result` assembly itself. It emits the multi-error `Result` variants (2–8 error types) and the matching `VoidResult` variants: the `Match`/`Map`/`Bind` arity overloads the library ships.

### Internationalization: Country / Currency / Language code generators
*Assembly:* `Pragmatic.Internationalization.SourceGenerator`.

Three generators that emit static ISO code tables from embedded JSON resources. Each is gated by an **`AdditionalFiles` marker file** (empty file with a magic name):

| Generator | Marker file | Output |
|-----------|-------------|--------|
| `CountryCodeGenerator` | `.generate-countries` | static `CountryCode` properties (ISO 3166-1) |
| `CurrencyCodeGenerator` | `.generate-currencies` | static `CurrencyCode` properties (ISO 4217) |
| `LanguageCodeGenerator` | `.generate-languages` | static `LanguageCode` properties (ISO 639) |

Without the marker file, the generator stays silent, so apps pay nothing unless they opt in.

### CSV: `CsvSourceGenerator`
*Assembly:* `Pragmatic.Documents.Csv.Generator`.

**Trigger:** `[CsvSerializable]` on a type. **Generates:** a nested `Csv` class with typed `Write`/`Read` methods, with no reflection and AOT-safe.

### Client: `PragmaticClientGenerator`
*Assembly:* `Pragmatic.Client.SourceGenerator`.

Reads the API manifest rather than your domain, and generates a typed HTTP client for it: one method per operation, typed errors included.

### Testing: contract tests, mocks, comparers
*Assemblies:* `Pragmatic.Testing.SourceGenerator`, `Pragmatic.Testing.Mocking.SourceGenerator`, `Pragmatic.Testing.Comparers.SourceGenerator`.

These run in the **test** project. `ContractTestGenerator` writes a test per endpoint contract (authorization, CRUD, state transitions) from the endpoints it can see; `MockGenerator` writes a compiled mock for every `[assembly: GenerateMock<T>]` (no dynamic proxy, AOT-safe); `ComparerGenerator` writes a member-by-member `BeEquivalentTo` for every `[assembly: GenerateComparer<T>]`.

## See also

- [How it works](/source-generator/how-it-works/): the detect → transform → template → emit pipeline.
- [Feature detection](/source-generator/feature-detection/): marker types and the `DetectedFeatures` flags.
- [Diagnostics](/reference/diagnostics/): the full `PRAG` ID reference.
