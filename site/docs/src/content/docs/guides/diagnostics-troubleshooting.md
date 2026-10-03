---
title: "Diagnostics & Troubleshooting"
description: "> Complete reference for all PRAG diagnostics emitted by the Pragmatic source generators and analyzers."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/docs/howto/diagnostics-troubleshooting.md
sidebar:
  order: 10
---
> Complete reference for all PRAG diagnostics emitted by the Pragmatic source generators and analyzers.

---

## 1. Overview

### What Are PRAG Diagnostics?

Pragmatic.Design uses **Roslyn source generators** and **Roslyn analyzers** to generate code at compile time.
When the SG encounters invalid configurations, missing attributes, or suboptimal patterns, it emits
diagnostics with IDs in the `PRAG####` format.

These diagnostics appear:
- In the **Error List** window in Visual Studio / Rider
- In the **build output** of `dotnet build`
- In CI logs

### Severity Levels

| Severity | Build Effect | When Used |
|----------|-------------|-----------|
| **Error** | Fails the build | Invalid attribute usage, missing requirements, incompatible code |
| **Warning** | Build succeeds with warning | Suboptimal patterns, deprecated usage, performance concerns |
| **Info** | Build succeeds silently (unless verbose) | Suggestions, hints, informational messages |
| **Hidden** | Not shown in output | Refactoring opportunities, code fix triggers |

### How to Suppress a Diagnostic

If you intentionally want to suppress a diagnostic, use any of these approaches:

```xml
<!-- In .csproj: suppress globally -->
<PropertyGroup>
  <NoWarn>$(NoWarn);PRAG0303</NoWarn>
</PropertyGroup>
```

```csharp
// In code: suppress locally
#pragma warning disable PRAG0303
public partial class MyDto { /* ... */ }
#pragma warning restore PRAG0303
```

```ini
# In .editorconfig: configure severity
[*.cs]
dotnet_diagnostic.PRAG0303.severity = none
```

> **Tip**: Avoid suppressing Error-level diagnostics. They indicate real problems that will cause
> incorrect or missing generated code.

---

## 2. Quick Reference Table

### Source Generator Diagnostics

| ID | Module | Severity | Title | Quick Fix |
|----|--------|----------|-------|-----------|
| PRAG0200 | Validation | Error | Type must be partial | Add `partial` keyword |
| PRAG0201 | Validation | Error | Validator must implement IValidator\<T\> | Implement the interface |
| PRAG0203 | Validation | Error | Comparison property not found | Check `nameof()` reference |
| PRAG0204 | Validation | Warning | ValidateElements on non-collection | Remove attribute or change type |
| PRAG0205 | Validation | Error | ValidateElements element type not validatable | Add validation attrs to element type |
| PRAG0209 | Validation | Warning | Incompatible comparison types | Use compatible property types |
| PRAG0300 | Mapping | Error | Type must be partial | Add `partial` keyword |
| PRAG0302 | Mapping | Error | Property/path not found on source or Target | Fix the `[MapProperty]` source path or `Target=` segment |
| PRAG0303 | Mapping | Warning | No matching source property | Add `[MapProperty]` or `[MapIgnore]` |
| PRAG0304 | Mapping | Error | Incompatible simple types (no conversion) | Add converter or change types |
| PRAG0305 | Mapping | Error | Converter must implement IValueConverter | Implement correct interface |
| PRAG0306 | Mapping | Error | Converter needs parameterless constructor | Add public parameterless ctor |
| PRAG0307 | Mapping | Warning | Required property not mapped | Map the property or remove `required` |
| PRAG0309 | Mapping | Error | Nested type missing MapFrom | Add `[MapFrom<T>]` to nested type |
| PRAG0310 | Mapping | Error | GenerateProjection requires MapFrom | Add `[MapFrom<T>]` first |
| PRAG0313 | Mapping | Info | Circular reference detected | Informational -- instance tracking used |
| PRAG0314 | Mapping | Error | Conflicting MapIgnore and MapProperty | Remove one attribute |
| PRAG0315 | Mapping | Error | Nested DTO [MapFrom<T>] unrelated to navigation type | Align the nested DTO's source type |
| PRAG0316 | Mapping | Error | No suitable constructor | Add constructor matching init-only props |
| PRAG0317 | Mapping | Error | Nullable to non-nullable without Default (no auto-default) | Add `Default = ...` to attribute |
| PRAG0319 | Mapping | Warning | CustomizeMapping ignored in Projection | Use non-projection mapping |
| PRAG0320 | Mapping | Warning | MapConverter not supported in Projection | Remove converter for projection |
| PRAG0321 | Mapping | Info | Format not supported in Projection | Simplify format for SQL |
| PRAG0322 | Mapping | Info | Complex dictionary not supported | Use `[MapIgnore]` |
| PRAG0323 | Mapping | Warning | Direct match AND flattening both apply | Add explicit `[MapProperty]` (FK convention `{Nav}Id` excluded) |
| PRAG0324 | Mapping | Info | ID property excluded from ToEntity | Use `[MapProperty]` to include |
| PRAG0325 | Mapping | Hidden | Source property not mapped to DTO (data dropped) | Add the property or map it explicitly; raise severity via .editorconfig |
| PRAG0326 | Mapping | Warning | Nested projection drops converter/format member | Project the nested DTO via its own `.Projection` |
| PRAG0327 | Mapping | Warning | Nested projection truncated by MaxDepth | Raise `[GenerateProjection(MaxDepth = ...)]` |
| PRAG0328 | Mapping | Error | Enum member missing on target enum | Add the member or use a converter |
| PRAG0329 | Mapping | Error | [MapCondition] predicate missing/invalid | Provide a static bool method taking the source type |
| PRAG0330 | Mapping | Error | Invalid [MapDerived] pair | Derived source must derive the [MapFrom] source; derived DTO must derive the base DTO |
| PRAG0331 | Mapping | Info | [MapDerived] ignored in Projection | Query derived DTOs explicitly |
| PRAG0332 | Mapping | Info | [MapCondition] ignored in Projection | Condition applies to FromEntity only |
| PRAG0400 | Actions | Error | Action class must be partial | Add `partial` keyword |
| PRAG0401 | Actions | Error | Must inherit DomainAction base | Inherit `DomainAction<T>` or `VoidDomainAction` |
| PRAG0403 | Actions | Error | `ReturnType = LogicalKey` the mutation cannot type | Return `Id` or `Entity`, or declare the key's parts on the entity's own properties |
| PRAG0404 | Actions | Error | LoadEntity ID property not found | Add the ID property to the action |
| PRAG0405 | Actions | Error | LoadEntity key type not determined | Ensure entity has `[Entity]` |
| PRAG0406 | Actions | Error | Boundary must be partial | Add `partial` to `[Boundary]` class |
| PRAG0407 | Actions | Error | Boundary must be in namespace | Move class into a namespace |
| PRAG0409 | Actions | Error | Mutation must inherit Mutation\<T\> | Inherit `Mutation<TEntity>` |
| PRAG0410 | Actions | Error | Mutation mode not determined | Add `[Mutation(Mode = ...)]` or use Create/Update prefix |
| PRAG0412 | Actions | Warning | SubBoundary nesting too deep | Flatten namespace structure |
| PRAG0413 | Actions | Info | SubBoundary inferred from namespace | Informational |
| PRAG0414 | Actions | Warning | Mutation property has no `Set{Prop}()` on the entity | Add the setter method or `[MapIgnore]` the property |
| PRAG0415 | Actions | Warning | `[AuthorizationPolicy]` type could not be resolved | Check the policy type exists and is accessible |
| PRAG0418 | Actions | Warning | `[RequirePermission(const)]` unresolvable -- **fail-open** | Use a literal string or a generated entity permission |
| PRAG0419 | Actions | Warning | Concrete field type: cannot tell an injected service from state -- **not injected** | Depend on an interface/abstract type, or mark the type `[Service]` |
| PRAG0420 | Actions | Warning | `[ResiliencePolicy]` with an empty name -- attribute ignored | Provide the policy name |
| PRAG0421 | Actions | Warning | `[ExplicitPermission(Constant)]` not in this compilation's catalog -- derived name used | Name a generated constant (entity CRUD or `[assembly: Permission]`), or pass the name as a string |
| PRAG0422 | Actions | Error | `[RequirePermission]` with no permissions -- **fail-open** | Name at least one permission, or remove the attribute |
| PRAG0423 | Actions | Error | `[StartsDelegation]` subject property not found | Use `nameof()` on a string property of the action |
| PRAG0424 | Actions | Warning | Writes in its own boundary **and** calls another -- inner commit survives a later failure | Raise a domain event; or `[UndoWith<T>]` on the step; or `[AcceptsPartialWrites("reason")]` |
| PRAG0425 | Actions | Error | `[UndoWith<T>]` names a type that does not compensate this action | Implement `ICompensates<TResult>` / `ICompensatesVoid` |
| PRAG0426 | Actions | Error | `[Transactional]` action calls another boundary -- the rollback cannot reach it | Raise an event, or drop `[Transactional]` and answer PRAG0424 |
| PRAG0427 | Actions | Warning | `[CompositeAction]` with no steps | Add steps, or remove the attribute |
| PRAG0428 | Actions | Warning | Composes actions/mutations without saying how they commit | `[Transactional]`, `[CommitStrategy(Once)]` or `[CommitStrategy(PerStep)]` |
| PRAG0429 | Actions | Info | The undo across a boundary is a saga's step without a saga's durability | Informational -- publish an event or model it as a saga where the crash window matters |
| PRAG0430 | Actions | Warning | `[CommitStrategy(PerStep)]` on a `[CompositeAction]` -- a composite commits once by construction | Remove it, or compose in the body instead of declaring steps |
| PRAG0431 | Actions | Warning | `[Transactional]` on a `[Boundary]` -- a transaction is opened per invocation, not per boundary | Declare it on the actions that need it; `[CommitStrategy]` is what a boundary can say |
| PRAG0432 | Actions | Error | `[Transactional]`/`[CommitStrategy]` on an action with no boundary -- no unit of work, so the attribute does nothing | Name the boundary with `[BelongsTo<T>]` |
| PRAG0433 | Actions | Warning | A `[Raises<T>]` event parameter matches no property -- it is dispatched as `default` | Rename the parameter to match, or add the property |
| PRAG0500 | Endpoints | Error | Endpoint must be partial | Add `partial` keyword |
| PRAG0501 | Endpoints | Error | Not a recognised endpoint shape | Inherit one of the bases the message lists, or carry `[Query<TEntity, TResult>]` |
| PRAG0502 | Endpoints | Error | Route is required | Add route to `[Endpoint]` attribute |
| PRAG0503 | Endpoints | Error | Too many error types | Reduce to 6 or fewer |
| PRAG0504 | Endpoints | Warning | Route parameter not found | Add matching public property |
| PRAG0505 | Endpoints | Error | Duplicate endpoint name | Give each endpoint a unique `[Endpoint(Name = "...")]` |
| PRAG0507 | Endpoints | Error | Endpoint group not found, or not a group | Make the type in `[EndpointGroup<X>]` exist and decorate it with `[EndpointGroup]` |
| PRAG0512 | Endpoints | Info | Implicit body binding | Add `[FromBody]`/`[FromQuery]`/`[FromRoute]` |
| PRAG0513 | Endpoints | Warning | `[Idempotent]` on a safe verb | Remove it -- GET/HEAD/OPTIONS are idempotent by definition |
| PRAG0514 | Endpoints | Warning | HEAD endpoint declares a response type | Use `VoidEndpoint` -- the body is suppressed |
| PRAG0515 | Endpoints | Error | Autocomplete missing key | Add `Id` or `[Key]` property to entity |
| PRAG0516 | Endpoints | Error | Invalid `[MaxFileSize]` limit | Use a positive byte limit (≤ 0 rejects every upload) |
| PRAG0517 | Endpoints | Error | Invalid `[MaxBodySize]` limit | Use a positive byte limit |
| PRAG0518 | Endpoints | Warning | Invalid example JSON | Fix the JSON in `[RequestExample]`/`[ResponseExample]` |
| PRAG0520 | Endpoints | Error | `[ResponseCache]` on a streaming endpoint | Remove it -- SSE cannot be cached |
| PRAG0521 | Endpoints | Error | Streaming endpoint verb must be GET or POST | Change the verb |
| PRAG0522 | Endpoints | Error | Status override on a streaming endpoint | Remove `[HttpStatus]`/`[CreatedAt]` -- SSE always opens 200 |
| PRAG0523 | Endpoints | Warning | Versioning on a streaming endpoint | Only the default version is generated |
| PRAG0524 | Endpoints | Error | `[PostProcessor]` on a streaming endpoint | Remove it -- there is no materialized result |
| PRAG0526 | Endpoints | Warning | ApiRoutes member name collision | Set a distinct `Name` on `[Endpoint]` |
| PRAG0527 | Endpoints | Warning | Concrete field type: cannot tell an injected service from state -- **not injected** | Depend on an interface/abstract type, or mark the type `[Service]` |
| PRAG0529 | Endpoints | Error | Two endpoints on the same verb and route -- the route answers 500 per request | Change one of the two routes |
| PRAG0531 | Endpoints | Error | `[ReturnsDto<T>]` names a DTO that cannot be built from the mutation's entity -- there is no `FromEntity` to call | Add `[MapFrom<TEntity>]` to the DTO; a `{Entity}ReadDto` scaffolded by `[Resource]` needs nothing |
| PRAG0532 | Endpoints | Error | A GET operation has a nested object among its properties -- a query string carries scalars only | Make it a scalar, take it as JSON in one value, or expose the operation on a verb with a body |
| PRAG0533 | Endpoints | Error | A create answers with a DTO that reads through a navigation to another aggregate -- nothing is loaded to read it from | Answer with a DTO of this aggregate alone, or read the fuller shape back with a query |
| PRAG0534 | Endpoints | Error | A `[PreProcessor<T>]`/`[PostProcessor<T>]` type the container cannot construct -- no registration is generated for it | Name a concrete processor with a public constructor |
| PRAG0535 | Endpoints | Error | `[ReturnsDto<T>]` beside `ReturnType = Id` or `LogicalKey` -- the key answers and the DTO is read by nothing | Remove one of the two: the DTO applies only when the mutation returns the entity |
| PRAG0536 | Endpoints | Error | An optional header, query, claim or cookie value on an `init` property whose initializer is not a constant -- the generated endpoint repeats the default in the object initializer, and cannot repeat a call | Give the property a constant default, or a `set` accessor |
| PRAG0550 | Endpoints | Error | Autocomplete requires string | Apply to string properties only |
| PRAG0551 | Endpoints | Warning | Versioning requires Asp.Versioning.Http | Add NuGet package reference |
| PRAG0620 | Persistence | Warning | State machine missing initial state | Add `[InitialState]` to one value |
| PRAG0621 | Persistence | Warning | Unreachable state | Add `[TransitionFrom]` or `[InitialState]` |
| PRAG0622 | Persistence | Error | Invalid transition source | Use valid enum member name |
| PRAG0651 | Persistence | Warning | Property may need value converter | Register a ValueConverter |
| PRAG0710 | Query | Warning | DTO nav without Include | Check property name matches entity nav |
| PRAG0711 | Query | Warning | Deep Include depth on loading profile | Reduce MaxDepth or use Projection |
| PRAG0716 | Query | Warning | DTO with many navigation levels | Use Projection strategy |
| PRAG0800 | Messaging | Error | Handler must implement IMessageHandler\<T\> | Add interface implementation |
| PRAG0801 | Messaging | Error | Handler must be partial | Add `partial` keyword |
| PRAG0802 | Messaging | Error | Invalid retry config | Set MaxAttempts > 0 |
| PRAG0803 | Messaging | Error | Middleware must implement IMessageMiddleware | Add interface implementation |
| PRAG0811 | Messaging | Info | Saga state has no handler | Add handler or confirm terminal |
| PRAG0813 | Messaging | Error | Saga state must be enum | Change type parameter to enum |
| PRAG0814 | Messaging | Error | Saga missing start handler | Add `[SagaStart]` to one method |
| PRAG0816 | Messaging | Warning | Event without consumers | Add handler or remove event |
| PRAG0819 | Messaging | Warning | Multiple `[PartitionKey]` properties | Keep exactly one per message type |
| PRAG0820 | Messaging | Error | Saga event has no correlation | Implement `ICorrelatedMessage` or mark a `[CorrelationKey]` |
| PRAG0821 | Messaging | Warning | Multiple `[CorrelationKey]` properties | Keep exactly one per event type |
| PRAG0822 | Messaging | Warning | Domain-event cascade cycle (analyzer) | Make a handler idempotent/terminal or guard the re-raise |
| PRAG0831 | Messaging | Warning | `[EnableOutbox]` without Messaging.EFCore -- no-op | Add the Pragmatic.Messaging.EFCore reference |
| PRAG0832 | Messaging | Warning | `[EnableSagaPersistence]` without Messaging.EFCore -- no-op | Add the Pragmatic.Messaging.EFCore reference |
| PRAG0833 | Messaging | Warning | `[EnableOutbox]` + `[EnableEventOutbox]` on one boundary | Keep exactly one -- both clear the same events |
| PRAG0834 | Messaging | Warning | `[EnableBatchProgress]` on more than one boundary | Keep it on the single boundary hosting `__BatchProgress` |
| PRAG0835 | Messaging | Warning | `[EnableBatchProgress]` without Messaging.Batch -- no-op | Add the Pragmatic.Messaging.Batch reference |
| PRAG0905 | Temporal | Warning | Timezone attribute on an unsupported property type | Apply it to `DateTimeOffset`/`DateTime` (or nullable) |
| PRAG1001 | Identity | Error | Duplicate permission name | Declare each value once; a CRUD permission is already generated |
| PRAG1003 | Identity | Error | IRole.Name must be non-empty | Return non-empty string from Name |
| PRAG1004 | Identity | Error | A declared permission names no boundary of this assembly | Start the value with its boundary |
| PRAG1005 | Identity | Error | A declared permission's constant would take a name already in use | Pick segments that do not reuse a name of the class |
| PRAG1050 | Composition | Error | Duplicate UsePackage | Remove duplicate declaration |
| PRAG1104 | Ownership | Info | OwnerId manually declared | Informational -- SG skips generation |
| PRAG1601 | Composition | Error | `[IncludeModule<T>]` names no known module | Fix the reference |
| PRAG1602 | Composition | Error | Circular dependency | Remove circular module reference |
| PRAG1603 | Composition | Error | A hosted module's dependency is not hosted | `[Include<T>]` it, or `[RemoteBoundary<T>]` |
| PRAG1607 | Composition | Warning | Two DB-bound modules resolve to the same name | Rename a boundary or use `[Include<TModule, TDb, TDbContext>]` |
| PRAG1608 | Composition | Warning | Included module not discovered -- DbContext NOT registered | Ensure the persistence SG ran, or use the 3-arity `[Include]` |
| PRAG1609 | Composition | Warning | Relational database without `ConfigKey` | Set `[PragmaticDatabase(ConfigKey = "...")]` |
| PRAG1610 | Composition | Error | Incompatible schema version | Update packages to same major version |
| PRAG1611 | Composition | Warning | Newer schema version | Update Pragmatic.Composition package |
| PRAG1612 | Composition | Info | Legacy schema version | Informational -- compat mode |
| PRAG1630 | Composition | Error | StartupStep must implement IStartupStep | Implement the interface |
| PRAG1631 | Composition | Error | StartupStep must be on a class | Move attribute to class |
| PRAG1632 | Composition | Error | NeedsStep references unavailable type | Add required package reference |
| PRAG1640 | Composition | Error | Service requires class | Move `[Service]` to a class |
| PRAG1641 | Composition | Warning | Dependency not registered | Register or add `[Service]` |
| PRAG1642 | Composition | Warning | Lifetime mismatch (captive dependency) | Align lifetimes |
| PRAG1643 | Composition | Warning | No interface found | Use `AsSelf=true` or add interface |
| PRAG1645 | Composition | Error | Abstract class cannot be service | Remove `abstract` or `[Service]` |
| PRAG1646 | Composition | Warning | Keyed services require .NET 8+ | Upgrade to .NET 8+ or remove Key |
| PRAG1647 | Composition | Warning | `[Inject]` members ignored on an open-generic service | Use constructor parameters |
| PRAG1651 | Composition | Warning | Boundary without database | Use `[Include<T, TDb>]` |
| PRAG1652 | Composition | Error | DbContext name collision | Use distinct DbContext types |
| PRAG1660 | Composition | Error | Decorator must implement interface | Implement at least one interface |
| PRAG1661 | Composition | Error | Decorator missing inner service | Add ctor parameter of decorated type |
| PRAG1670 | Composition | Error | EventHandler missing interface | Implement `IDomainEventHandler<T>` |
| PRAG1680 | Composition | Warning | ExposeEndpoint not from package | Use `[Endpoint]` directly |
| PRAG1685 | Composition | Error | RemoteBoundary overlaps Include | Remove one declaration |
| PRAG1686 | Composition | Warning | RemoteBoundary has no actions | Add actions or remove attribute |
| PRAG1687 | Composition | Info | RemoteBoundary base URL not configured | Configure at runtime |
| PRAG1688 | Composition | Warning | Config key missing from appsettings | Add key to appsettings.json |
| PRAG1689 | Composition | Warning | `[UndoWith<T>]` on an action this host reaches over HTTP -- the undo runs in the other process | Publish an event, or model the undo as a saga |
| PRAG1690 | Composition | Info | Discovered Pragmatic modules | Informational |
| PRAG1691 | Composition | Info | Module composition info | Informational |
| PRAG1693 | Composition | Info | No services discovered | Check `[Service]` usage |
| PRAG1694 | Composition | Info | No startup steps discovered | Informational |
| PRAG1695 | Composition | Error | Authorization without Identity, and no `[AnonymousHost]` | Add Pragmatic.Identity.AspNetCore, or declare `[AnonymousHost]` on the host module |
| PRAG1696 | Composition | Warning | Identity.Persistence without Authorization | Add Pragmatic.Authorization reference |
| PRAG1700 | Caching | Error | Type must be partial | Add `partial` keyword |
| PRAG1701 | Caching | Error | Invalid cache duration | Use `5m`, `1h`, `1d` format |
| PRAG1702 | Caching | Error | No cache key properties | Add at least one public property |
| PRAG1703 | Caching | Error | Invalid placeholder in tag/key | Check placeholder name matches property |
| PRAG1704 | Caching | Error | `[InvalidatesCache]` type must be partial | Add `partial` keyword |
| PRAG1750 | Caching | Warning | Duplicate cache key order | Assign unique Order values |
| PRAG1751 | Caching | Warning | All properties excluded | Keep at least one property |
| PRAG1800 | I18n | Error | Invalid translation file | Fix JSON syntax |
| PRAG1801 | I18n | Warning | Duplicate translation key | Consolidate into single file |
| PRAG1802 | I18n | Warning | Missing translation key | Add key to target culture |
| PRAG1803 | I18n | Info | Empty translation file | Add key-value pairs |
| PRAG1900 | Documents | Warning | CSV property type is not round-trippable | Use a supported type or `[CsvColumn(Ignore = true)]` |
| PRAG2000 | Configuration | Error | Configuration class must be partial | Add `partial` keyword |
| PRAG2001 | Configuration | Error | Cannot be static or abstract | Remove modifier |
| PRAG2050 | Configuration | Warning | Required property has default | Reconsider `[Required]` usage |
| PRAG2200 | Patch | Error | Patch type must be partial | Add `partial` keyword |
| PRAG2201 | Patch | Error | Entity type not found | Check the `[GeneratePatch<T>]` type argument |
| PRAG2202 | Patch | Warning | No patchable properties | Add settable properties |
| PRAG2500 | Jobs | Error | Must implement IJob or IJob\<T\> | Add interface |
| PRAG2501 | Jobs | Error | Invalid cron expression | Provide valid cron string |
| PRAG2502 | Jobs | Warning | Job should be partial | Add `partial` keyword |
| PRAG2503 | Jobs | Error | Duplicate recurring job ID | Use unique ID |
| PRAG2504 | Jobs | Error | Invalid retry MaxAttempts | Set MaxAttempts > 0 |
| PRAG2505 | Jobs | Error | Continuation must be a job | Implement IJob on continuation type |
| PRAG2506 | Jobs | Error | Continuation cycle detected | Remove cyclic `[ContinueWith]` |
| PRAG2507 | Jobs | Error | A second schedule has to name itself | Add `Id = "..."` to the extra `[RecurringJob]` |
| PRAG2601 | Traits | Warning | Trait without `[Resource]` -- no endpoints generated | Add `[Resource("segment")]` to the parent entity |
| PRAG2602 | Resource | Error | `[Resource]` segment must be kebab-case | Use lowercase letters, digits and hyphens |
| PRAG2603 | Resource | Error | Duplicate resource segment in a boundary | Give each entity a unique segment |
| PRAG2605 | Resource | Info | Capabilities without `Read` | Add `ResourceCapabilities.Read` if `GET /{segment}/{id}` is needed |
| PRAG2700 | ValueObject | Warning | `[ValueObject]` type must be partial | Add `partial` keyword |
| PRAG2701 | ValueObject | Warning | `[ValueObject]` missing `private static Validate` | Declare the method so `Create` can be generated |
| PRAG2750 | Lifecycle | Error | `[Raises<T>]` requires `DomainEventSource` | Derive the entity from `DomainEventSource` |
| PRAG2751 | Lifecycle | Warning | Event ctor parameter unmatched -- passed `default` | Rename it to match an entity member |
| PRAG2752 | Lifecycle | Warning | `[EnableEventOutbox]` without Events.EFCore -- no-op | Add the Pragmatic.Events.EFCore reference |
| PRAG2753 | Lifecycle | Error | `[Raises<T>]` on an entity's **method** -- nothing generates that raise | Declare it on the entity class, or use `[RaisesEvent<T>]` on the state machine's target member, or `RaiseEvent(...)` in the body |
| PRAG9000 | SG infra | Error | A generator output failed | Report the triggering code; other outputs are unaffected |

### Roslyn Analyzer Diagnostics

These run over **your** code, not over generated code.

| ID | Assembly | Severity | Title | Quick Fix |
|----|----------|----------|-------|-----------|
| PRAG0001 | Result.Analyzers | Warning | Unsafe `Result.Value` access | Check `IsSuccess` (or match) before reading `.Value` |
| PRAG0002 | Result.Analyzers | Warning | Error type with custom properties is not `partial` | Add `partial` so `WriteExtensions` is generated |
| PRAG0200 / 0300 / 0400 / 0406 / 0500 / 0801 / 1700 / 2000 / 2200 / 2502 | SourceGenerator.Analyzers | Error (2502: Warning) | "Type must be partial" mirrors, so the IDE can offer the code fix | Add `partial` |
| PRAG0600 | SourceGenerator.Analyzers | Error | Persistence type (`[Entity]`, `[Repository]`, ...) must be partial | Add `partial` |
| PRAG0602 | SourceGenerator.Analyzers | Error | `[Database]` context must be partial | Add `partial` |
| PRAG0680 | Persistence.Analyzers | Warning | Use `Entity.Create()` instead of `new` | Replace `new Entity()` with `Entity.Create()` |
| PRAG0681 | Persistence.Analyzers | Warning | Do not use `default` for entities | Use `Entity.Create()` factory |
| PRAG0682 | Persistence.Analyzers | Warning | Do not use `Activator.CreateInstance` | Use `Entity.Create()` factory |
| PRAG0683 | Persistence.Analyzers | Warning | Entity declares a behavior method | Move the state change into a mutation or domain action |
| PRAG0684 | Persistence.Analyzers | Warning | Non-constant SQL in `FromSqlRaw`/`ExecuteSqlRaw` | Use interpolated `FromSql`/`ExecuteSql`, or pass parameters |
| PRAG0685 | Persistence.Analyzers | Info | Aggregate with many child collections | Split it, or reference other aggregates by id |
| PRAG0686 | Persistence.Analyzers | Warning | Cross-boundary DbContext reach-in | Call that boundary's actions/queries instead |
| PRAG0822 | SourceGenerator.Analyzers | Warning | Domain-event cascade cycle | Make a handler idempotent/terminal or guard the re-raise |
| PRAG0900--0904 | Temporal.Analyzers | Warning (0904: Info) | `DateTime.Now`/`Today`, missing `DateTimeKind`, raw `DateTimeOffset` comparison, time in tests | Use `IClock` or `TimeProvider`, pass a `DateTimeKind`, compare in UTC |
| PRAG1100 | SourceGenerator.Analyzers | Error | `[HasOwner]` requires a partial class | Add `partial` |
| PRAG1450 | Abstractions.Analyzers | Warning | Captive dependency: scoped service in a singleton | Inject the factory named in the message |
| PRAG1451 | Abstractions.Analyzers | Warning | `BuildServiceProvider()` creates a second container | Resolve from the app's provider |
| PRAG1452 | Abstractions.Analyzers | Warning | Optional `[Inject]` -- a missing service is injected as `null` | Set `Required = true` to fail fast at startup |
| PRAG2800 | Abstractions.Analyzers | Info | Serialized type not covered by a `JsonSerializerContext` | Add `[JsonSerializable(typeof(T))]` |

> Optional injection is `PRAG1452`, not `PRAG1647`. The composition generator emits `PRAG1647` for
> the open-generic `[Inject]` warning above, so a `NoWarn` or `#pragma` on `PRAG1647` does not quiet
> optional injection; it silences the warning telling you `[Inject]` members were ignored outright.

### Suppressors (PRAGS001--PRAGS004)

The generator also ships `DiagnosticSuppressor`s that switch off compiler/analyzer warnings which are
false alarms in generated code. They never fire outside those conditions -- there the original warning
is a genuine finding and stays visible.

| ID | Suppresses | Fires when |
|----|-----------|-----------|
| PRAGS001 | `CS8618` (non-nullable not initialized) | The member is a **property** whose setter is non-public or `init`, on a `partial` `[Entity]` type -- exactly what the generated `Create()` factory and trait templates assign. Public setters and fields are yours; the warning stays. |
| PRAGS002 | `CA1822` (can be marked `static`) | The type is `partial` and Pragmatic-decorated (`[Entity]`, `[DomainAction]`, `[Mutation]`, `[Query]`, `[MapFrom]`, `[MapTo]`) **and** either the member is in a generated file, or it is one half of a `partial` method. Hand-written methods that ignore instance state are still reported. |
| PRAGS003 | `CA1062` (validate public arguments) | The location is inside a `*.g.cs` / `*.generated.cs` file of a Pragmatic-decorated type. In a hand-written file the warning stays, even in the other half of the same partial type. |
| PRAGS004 | `IDE0051` (private member unused) | The type is `partial` and Pragmatic-decorated, so the generated half may reference the member. Without `partial` an unused private member is genuinely dead. |

---

## 3. Per-Module Diagnostic Details

### 3.1 Validation (PRAG0200--PRAG0209)

#### PRAG0200 -- Type with validation attributes must be partial

**What it means**: You applied validation attributes (e.g., `[Required]`, `[MinLength]`) to properties
on a class that is not declared as `partial`.

**Why it triggers**: The SG needs to add a `Validate()` method to the class, which requires `partial`.

**How to fix**:
```csharp
// Before
public class CreateUserRequest
{
    [Required] public string Name { get; set; }
}

// After
public partial class CreateUserRequest
{
    [Required] public string Name { get; set; }
}
```

#### PRAG0201 -- Validator must implement IValidator\<T\>

**What it means**: A class marked with `[Validator]` does not implement `IValidator<T>`.

**How to fix**:
```csharp
[Validator]
public class UserValidator : IValidator<CreateUserRequest>
{
    public ValidationResult Validate(CreateUserRequest instance) { /* ... */ }
}
```

#### PRAG0203 -- Comparison property not found

**What it means**: An attribute like `[EqualTo("OtherProp")]` or `[GreaterThanProperty("OtherProp")]`
references a property that does not exist on the same type.

**How to fix**: Check spelling and ensure both properties are on the same type.

#### PRAG0205 -- ValidateElements element type not validatable

**What it means**: `[ValidateElements]` was used on a collection whose element type does not have
validation attributes (i.e., does not implement `ISyncValidator`).

**How to fix**: Add validation attributes to the element type and make it `partial`.

---

### 3.2 Mapping (PRAG0300--PRAG0325)

#### PRAG0300 -- Type must be partial

**What it means**: A class with `[MapFrom<T>]` or `[MapTo<T>]` is not declared as `partial`.

**How to fix**: Add the `partial` keyword.

#### PRAG0303 -- No matching source property

**What it means**: A target DTO property has no corresponding property on the source type
(by name convention).

**How to fix**:
```csharp
// Option A: map explicitly
[MapProperty(From = nameof(User.FullName))]
public string DisplayName { get; set; }

// Option B: ignore explicitly
[MapIgnore]
public string ComputedField { get; set; }
```

**When to suppress**: If you intentionally set the property manually after mapping.

#### PRAG0304 -- Incompatible types

**What it means**: The SG cannot auto-convert between source and target property types
(e.g., `int` to `string`).

**How to fix**: Add a `[MapConverter<TConverter>]` attribute or change one of the types.

#### PRAG0307 -- Required property not mapped

**What it means**: A target property is marked `required` but has no source mapping.

**How to fix**: Either map the property explicitly or remove the `required` modifier.

#### PRAG0310 -- GenerateProjection requires MapFrom

**What it means**: `[GenerateProjection]` was applied without a corresponding `[MapFrom<T>]`.
Projection generation needs to know the source entity.

**How to fix**:
```csharp
[MapFrom<User>]
[GenerateProjection]
public partial class UserDto { /* ... */ }
```

#### PRAG0314 -- Conflicting attributes

**What it means**: A property has both `[MapIgnore]` and `[MapProperty]` which contradict each other.

**How to fix**: Remove one of the two attributes.

#### PRAG0317 -- Nullable to non-nullable without Default

**What it means**: A nullable source property maps to a non-nullable target without specifying a default.

**How to fix**:
```csharp
[MapProperty(Default = "\"\"")]  // default to empty string
public string Name { get; set; }
```

#### PRAG0325 -- Source property not mapped to DTO

**What it means**: A source entity property is not represented in the target DTO. This is a **Hidden**
diagnostic, useful for catching accidental omissions.

**How to configure**: Promote to Warning in `.editorconfig`:
```ini
dotnet_diagnostic.PRAG0325.severity = warning
```

---

### 3.3 Actions (PRAG0400--PRAG0418)

#### PRAG0400 -- Action class must be partial

**What it means**: A class using action attributes (e.g., `[DomainAction]`, `[Mutation]`) must be `partial`
so the SG can generate the invoker.

#### PRAG0401 -- Must inherit from DomainAction base

**What it means**: The action class does not inherit from `DomainAction<T>` or `VoidDomainAction`.

**How to fix**:
```csharp
public partial class GetUser : DomainAction<UserDto>
{
    public Guid UserId { get; set; }
    // ...
}
```

#### PRAG0404 -- LoadEntity ID property not found

**What it means**: `[LoadEntity<TEntity>]` specifies (or defaults to) an ID property name that
does not exist on the action class.

**How to fix**: Add the ID property or specify the correct property name:
```csharp
[LoadEntity<Reservation>(IdProperty = "ReservationId")]
public partial class UpdateReservation : Mutation<Reservation>
{
    public Guid ReservationId { get; set; }
}
```

#### PRAG0409 -- Mutation must inherit from Mutation\<TEntity\>

**What it means**: A class decorated with `[Mutation]` does not inherit from `Mutation<TEntity>`.

#### PRAG0410 -- Mutation mode not determined

**What it means**: The SG cannot determine whether this is a Create or Update mutation.

**How to fix**:
```csharp
// Option A: use a class name prefix
public partial class CreateReservation : Mutation<Reservation> { }
public partial class UpdateReservation : Mutation<Reservation> { }

// Option B: specify explicitly
[Mutation(Mode = MutationMode.Create)]
public partial class NewBooking : Mutation<Reservation> { }
```

#### PRAG0412 -- SubBoundary nesting deeper than 2 levels

**What it means**: The SG inferred a sub-boundary from namespace structure that is more than
2 levels deep. Deep nesting creates overly complex generated interfaces.

**When to suppress**: If the nesting is intentional and well-understood.

#### PRAG0418 -- Permission constant could not be resolved

**What it means**: `[RequirePermission(SomeConst)]` names a constant the generator cannot resolve to a
permission value. This is **fail-closed by design**: the diagnostic exists because an unresolved
constant would otherwise produce an action that looks protected but enforces nothing.

**How to fix**: Use a literal permission string, or a permission constant the generator itself
produces for the entity.

---

### 3.4 Endpoints (PRAG0500--PRAG0551)

#### PRAG0500 -- Endpoint class must be partial

**What it means**: A class with `[Endpoint]` must be `partial`.

#### PRAG0502 -- Route is required

**What it means**: The `[Endpoint]` attribute must include a route pattern.

**How to fix**:
```csharp
[Endpoint(HttpVerb.Get, "/api/users/{UserId}")]
public partial class GetUserEndpoint : Endpoint<UserDto> { }
```

#### PRAG0504 -- Route parameter not found

**What it means**: A `{parameter}` in the route does not match any public property on the endpoint.

**How to fix**: Add a matching property:
```csharp
[Endpoint(HttpVerb.Get, "/api/users/{UserId}")]
public partial class GetUserEndpoint : Endpoint<UserDto>
{
    public Guid UserId { get; set; }  // matches {UserId}
}
```

#### PRAG0505 -- Duplicate endpoint name

**What it means**: Two endpoints resolve to the same name, which breaks link generation.

**How to fix**: Rename one of the endpoint classes or specify a custom name in the attribute.

#### PRAG0515 / PRAG0550 -- Autocomplete diagnostics

**What they mean**: `[Autocomplete]` requires the entity to have an identifiable key property,
and the attribute must be placed on a `string` property.

**How to fix**:
```csharp
[Entity]
public partial class Amenity
{
    [Autocomplete]  // must be string
    public string Name { get; set; }
}
```

#### PRAG0551 -- Versioned methods require Asp.Versioning.Http

**What it means**: Your endpoint has versioned methods (`ExecuteV2`, `HandleAsyncV2`, etc.) but the
`Asp.Versioning.Http` NuGet package is not referenced. Without it, only the default version is generated.

**How to fix**: Add the NuGet package:
```xml
<PackageReference Include="Asp.Versioning.Http" Version="..." />
```

---

### 3.5 Persistence (PRAG0600--PRAG0651)

#### PRAG0600 / PRAG0602 -- Type must be partial

**What they mean**: a type carrying a Pragmatic persistence attribute (`[Entity]`, `[Repository]`, ...)
or a `[Database]` context type is not `partial`.

**Where they come from**: `Pragmatic.SourceGenerator.Analyzers`, not the generator. The analyzer is the
only source, and it is what makes the "make class partial" code fix available in the IDE.

#### PRAG0620--PRAG0622 -- State Machine diagnostics

**PRAG0620 -- Missing initial state**:
```csharp
public enum OrderStatus
{
    [InitialState]   // add this to fix PRAG0620
    Draft,

    [TransitionFrom(OrderStatus.Draft)]
    Confirmed,

    [TransitionFrom(OrderStatus.Confirmed)]
    Completed
}
```

**PRAG0621 -- Unreachable state**: the state has no `[TransitionFrom]` and is not the initial one.
Watch for the form that causes it silently: `TransitionFromAttribute` takes an `object`, so
`[TransitionFrom(nameof(Draft))]` compiles, and the generator ignores a string argument: it reads the
value only when its type is the enum.

**PRAG0622 -- Invalid transition source**: Ensure all `[TransitionFrom]` values match actual enum members.

---

### 3.6 Query Pipeline (PRAG0710--PRAG0716)

#### PRAG0710 -- DTO references a navigation without Include

**What it means**: a DTO property looks like a navigation but matches none on the entity, so
`[LoadWith]` will not include it and the property stays at its default.

**How to fix**: align the property name with the entity's navigation, or map it explicitly.

#### PRAG0711 -- Deep Include depth

**What it means**: A `[LoadWith]` profile with `MaxDepth > 3` will generate complex SQL.

**How to fix**: Reduce depth or switch to Projection strategy:
```csharp
[LoadWith<Order>(MaxDepth = 2)]      // reduce depth
[LoadWith<Order>(Strategy = QueryStrategy.Projection)]  // or use projection
```

---

### 3.7 Persistence Analyzers (PRAG0680--PRAG0686)

These are **Roslyn analyzers** (not SG diagnostics) in `Pragmatic.Persistence.Analyzers`.
They run on your application code to enforce the zero-reflection principle.

#### PRAG0680 -- Use Entity.Create() instead of `new`

**What it means**: You wrote `new Entity()` instead of using the generated factory.

**Why it matters**: `Entity.Create()` ensures Guid v7 ID generation, audit timestamps, and default values.

**How to fix**:
```csharp
// Before
var reservation = new Reservation();

// After
var reservation = Reservation.Create();
```

**When to suppress**: Never in application code. The analyzer already excludes generated code.

#### PRAG0681 -- Do not use `default` for entities

**What it means**: `default(Entity)` or `default` assigned to an entity variable bypasses initialization.

**How to fix**: Use `Entity.Create()` instead.

#### PRAG0682 -- Do not use Activator.CreateInstance

**What it means**: `Activator.CreateInstance<Entity>()` or `Activator.CreateInstance(typeof(Entity))`
creates an entity via reflection, bypassing the factory.

**How to fix**: Use `Entity.Create()`. Pragmatic follows a zero-reflection principle.

#### PRAG0684 -- Non-constant SQL in FromSqlRaw/ExecuteSqlRaw

**What it means**: the SQL string passed to `FromSqlRaw`/`ExecuteSqlRaw` is interpolated or concatenated
from runtime values -- a SQL injection hole.

**How to fix**: use the interpolated `FromSql`/`ExecuteSql` overloads (auto-parameterized), or pass the
values through the `parameters` argument.

#### PRAG0686 -- Cross-boundary DbContext reach-in

**What it means**: a type injects another boundary's `DbContext`. That couples the two schemas and
breaks module independence.

**How to fix**: call that boundary's actions/queries, or react to its integration events.

---

### 3.8 Messaging (PRAG0800--PRAG0835)

#### PRAG0800 -- Handler must implement IMessageHandler\<T\>

**How to fix**:
```csharp
[MessageHandler]
public partial class InvoicePaidHandler : IMessageHandler<InvoicePaid>
{
    public Task HandleAsync(InvoicePaid message, MessageContext ctx, CancellationToken ct)
    {
        // ...
    }
}
```

#### PRAG0802 -- Invalid retry configuration

**How to fix**:
```csharp
[MessageHandler]
[Retry(MaxAttempts = 3, DelaySeconds = 5)]  // MaxAttempts must be > 0
public partial class MyHandler : IMessageHandler<MyEvent> { }
```

#### PRAG0811--PRAG0821 -- Saga diagnostics

These validate the state graph and the correlation of `[Saga<TState>]` classes.

**PRAG0814 -- Saga has no start handler**:
```csharp
[Saga<BookingState>]
public partial class BookingSaga
{
    [SagaStart]  // required -- exactly one method
    public Task HandleStart(BookingRequested msg) { /* ... */ }

    [InState(BookingState.PaymentPending)]
    public Task HandlePayment(PaymentReceived msg) { /* ... */ }
}
```

**PRAG0820 -- Saga event has no correlation**: an event consumed by a saga must either implement
`ICorrelatedMessage` or mark one property with `[CorrelationKey]`. Without it the event cannot be routed
to a saga instance.

#### PRAG0831--PRAG0835 -- "the attribute is a no-op" diagnostics

These fire when a boundary opts into an EF-backed capability whose package is not referenced. Nothing
fails at build time and nothing works at runtime, which is exactly why they exist.

| Attribute | Missing reference | Consequence |
|---|---|---|
| `[EnableOutbox]` | `Pragmatic.Messaging.EFCore` | The outbox table is not mapped and no delivery pump is registered (PRAG0831) |
| `[EnableSagaPersistence]` | `Pragmatic.Messaging.EFCore` | `__SagaInstances`/`__SagaSteps` are not mapped (PRAG0832) |
| `[EnableBatchProgress]` | `Pragmatic.Messaging.Batch` | `__BatchProgress` is not mapped and no store is registered (PRAG0835) |

```xml
<PackageReference Include="Pragmatic.Messaging.EFCore" />
```

**PRAG0833** is the opposite problem: a boundary marked with both `[EnableOutbox]` (transport publish)
and `[EnableEventOutbox]` (in-process dispatch). Both capture and clear the same domain events, so one
silently wins. Keep exactly one.

**PRAG0834**: batch progress is a single store, unlike the per-boundary outbox and saga tables. Only one
boundary may carry `[EnableBatchProgress]`.

---

### 3.9 Identity / Authorization (PRAG1001, PRAG1003, PRAG1004, PRAG1005)

#### PRAG1001 -- Duplicate permission name

**What it means**: Two permission declarations resolve to the same value within the assembly, for example two
`[assembly: Permission]` lines, a line and a `[RequirePermission(..., Description = ...)]`, or a
declaration equal to an entity's CRUD permission (`billing.invoice.read`).

**How to fix**: Declare each value once. A CRUD permission is already generated: name its constant,
`BillingPermissions.Invoice.Read`.

#### PRAG1004 -- A declared permission names no boundary of this assembly

**What it means**: the first segment of a declared permission is none of the assembly's boundaries, so its
constant has no `{Boundary}Permissions` class to go into. An empty value is reported the same way.

**How to fix**: `{boundary}.{resource}.{verb}`, for example `[assembly: Permission("billing.invoice.refund", "Refund a paid invoice")]`.

#### PRAG1005 -- A declared permission's constant would take a name already in use

**What it means**: the constant's path is the value's segments in PascalCase, and one of them would name
something the class already has: `billing.invoice` is a constant `Invoice` beside the entity `Invoice`'s
class; `billing.invoice.invoice` a constant named like its enclosing class.

**How to fix**: pick a value whose segments do not reuse, at the same depth, the name of a resource, an
entity or another permission's verb.

#### PRAG1003 -- IRole.Name must be non-empty

**How to fix**:
```csharp
public sealed class Administrator : IRole
{
    public static string Name => "administrator";  // non-empty string literal
}
```

---

### 3.10 Data Ownership (PRAG1100, PRAG1104)

#### PRAG1100 -- OwnedEntity requires partial

**How to fix**: Add `partial` to the class declaration. This one comes from
`Pragmatic.SourceGenerator.Analyzers`, which is also what offers the code fix in the IDE.

#### PRAG1104 -- OwnerId manually declared

**What it means**: the type already declares `OwnerId`, so the generator skips generating it and emits
only the ownership filter. Nothing is broken.

**How to fix**: nothing is required. Remove the manual property to use the generated one.

---

### 3.11 Composition (PRAG1050, PRAG1601--PRAG1696)

#### PRAG1602 -- Circular dependency

**What it means**: Module A depends on Module B, which depends on Module A (directly or transitively).

**How to fix**: Refactor to break the cycle. Introduce a shared abstraction module if needed.

#### PRAG1603 -- Hosted module's dependency not hosted

**What it means**: the host hosts a module whose `[IncludeModule<T>]` dependency it neither includes nor
declares remote. The host registers only what it declares, so without this error the build succeeded and
the dependency failed when first resolved.

**How to fix**: add `[Include<T>]` for the dependency, or `[RemoteBoundary<T>]` if it runs in another host.

#### PRAG1608 -- Included module not discovered

**What it means**: `[Include<TModule, TDb>]` found no discovered module metadata for `TModule`, so its
DbContext is **not** registered. The build succeeds and the app fails at runtime.

**How to fix**: make sure the persistence source generator runs in that module (it needs the
`Pragmatic.Persistence.EFCore` reference), or use the explicit 3-arity
`[Include<TModule, TDb, TDbContext>]`.

#### PRAG1609 -- Database connection config key missing

**What it means**: a relational database has no `ConfigKey`, so the generated DbContext has no
connection string and fails at startup.

**How to fix**: `[PragmaticDatabase(ConfigKey = "ConnectionStrings:Booking")]`.

#### PRAG1642 -- Lifetime mismatch (captive dependency)

**What it means**: A Singleton service depends on a Scoped or Transient service, which causes the
shorter-lived service to be "captured" and held alive longer than intended.

**How to fix**:
```csharp
// Option A: make the dependent service Singleton too (if stateless)
[Service(Lifetime = Lifetime.Singleton)]
public class MyCacheService : ICacheService { }

// Option B: inject IServiceScopeFactory and resolve per-request
```

#### PRAG1652 -- DbContext name collision

**What it means**: Two different `[Database]` declarations generate DbContext classes with the same name.

**How to fix**: Use `[Include<TModule, TDatabase, TDbContext>]` with distinct DbContext type parameters.

#### PRAG1685 -- RemoteBoundary overlaps Include

**What it means**: A module is declared as both `[Include<T>]` (local) and `[RemoteBoundary<T>]` (remote).
A boundary must be one or the other.

#### PRAG1695 -- Authorization without Identity

**What it means**: The host references `Pragmatic.Authorization` (`Pragmatic.Actions` depends on it)
and not `Pragmatic.Identity`. Endpoints require authorization by default, and nothing in this host can
authenticate a request. It is an error because such a host cannot serve a single endpoint.

**How to fix**: Reference `Pragmatic.Identity.AspNetCore`. For a host that deliberately has no
authentication, declare it on the host module instead. The generated endpoint root then carries no
`RequireAuthorization()`:

```csharp
[Module]
[AnonymousHost]
[Include<CatalogModule, AppDatabase>]
public sealed class HostModule;
```

#### PRAG1696 -- Identity.Persistence without Authorization

`Pragmatic.Identity.Persistence` is referenced and `Pragmatic.Authorization` is not. Add the
`Pragmatic.Authorization` reference.

---

### 3.12 Caching (PRAG1700--PRAG1751)

#### PRAG1701 -- Invalid cache duration

**How to fix**:
```csharp
[Cacheable(Duration = "5m")]    // 5 minutes
[Cacheable(Duration = "1h")]    // 1 hour
[Cacheable(Duration = "1d")]    // 1 day
[Cacheable(Duration = "00:30:00")]  // TimeSpan format
```

#### PRAG1703 -- Invalid placeholder

**What it means**: A `{PropertyName}` placeholder in the cache key/tag template references a property
that does not exist on the type.

**How to fix**: Verify the placeholder matches an existing public property name.

---

### 3.13 Internationalization (PRAG1800--PRAG1803)

#### PRAG1800 -- Invalid translation file

**What it means**: A `*.json` translation file has syntax errors.

**How to fix**: Validate the JSON file. Common issues: trailing commas, unquoted keys, BOM encoding.

#### PRAG1802 -- Missing translation key

**What it means**: A key exists in the default culture (e.g., `en.json`) but is missing in another
culture (e.g., `it.json`). At runtime, the default culture value will be used as fallback.

**How to fix**: Add the missing key to the target culture file.

---

### 3.14 Patch (PRAG2200--PRAG2202)

#### PRAG2200 -- Patch type must be partial

**How to fix**:
```csharp
[GeneratePatch<Reservation>]
public partial class ReservationPatch { }
```

#### PRAG2201 -- Entity type not found

**What it means**: the entity type could not be resolved from the `[GeneratePatch<T>]` type argument.

**How to fix**: check that `T` names a real, accessible type and that the assembly declaring it is
referenced.

#### PRAG2202 -- No patchable properties

**What it means**: The target entity has no settable properties for patching.

**When to suppress**: If the entity intentionally uses only init-only or private-set properties.

---

### 3.15 Configuration (PRAG2000--PRAG2050)

#### PRAG2000 -- Configuration class must be partial

**How to fix**: Add `partial` to the class with `[Configuration]`.

#### PRAG2050 -- Required property has default value

**What it means**: A property marked `[Required]` also has a default value. The default satisfies
the requirement, making `[Required]` effectively meaningless.

**When to suppress**: If you want to enforce explicit configuration even when a default exists.

---

### 3.16 Jobs (PRAG2500--PRAG2507)

#### PRAG2500 -- Must implement IJob or IJob\<T\>

**How to fix**:
```csharp
[Job]
public partial class CleanupJob : IJob
{
    public Task ExecuteAsync(CancellationToken ct) { /* ... */ }
}
```

#### PRAG2501 -- Invalid cron expression

**How to fix**:
```csharp
[RecurringJob("0 2 * * *")]  // daily at 2 AM -- valid cron
public partial class NightlyCleanup : IJob { }
```

#### PRAG2503 -- Duplicate recurring job ID

**What it means**: Two `[RecurringJob]` classes resolve to the same ID (auto-generated from class name
or explicitly set).

**How to fix**:
```csharp
[RecurringJob("0 2 * * *", Id = "cleanup-orders")]
public partial class OrderCleanup : IJob { }

[RecurringJob("0 3 * * *", Id = "cleanup-logs")]
public partial class LogCleanup : IJob { }
```

#### PRAG2505 / PRAG2506 -- Continuation diagnostics

**PRAG2505**: The type referenced by `[ContinueWith<T>]` must implement `IJob` or `IJob<T>`.

**PRAG2506**: A cycle in the continuation chain was detected (e.g., A continues with B, B continues with A).

#### PRAG2507 -- A second schedule has to name itself

A job may carry several `[RecurringJob]` attributes: "every day at 02:00 **and** every Monday at 06:00"
is one job on two clocks, and each attribute becomes its own `RecurringJobDefinition`.

The id is what separates them, and only the **first** may rely on the one derived from the class name
(`ReportJob` -> `report`). A second unnamed schedule would take the same id, so the generator asks for
one instead of inventing it:

```csharp
[RecurringJob("0 2 * * *")]                                  // id: "report"
[RecurringJob("0 6 * * 1", Id = "report-weekly")]            // named, as it must be
public partial class ReportJob : IJob { ... }
```

Why not suffix by position: `report-2` is a name nobody chose, it is what an operator reads in the
schedule list, and it would renumber silently the day the attributes are reordered -- while the id is a
persisted key. `TimeZone` and `Misfire` belong to each schedule; `Priority` and `MaxConcurrency`
describe the job type and stay on the job.
Remove one `[ContinueWith]` to break the cycle.

---

### 3.17 Traits and Resource (PRAG2600--PRAG2605)

#### PRAG2601 -- Trait endpoint requires [Resource]

**What it means**: `[HasComments]`/`[HasTags]`/`[HasNotes]`/`[HasAttachments]` was applied to an entity
with no `[Resource]`. The trait entity, EF config and actions are still generated, but **no endpoints**
are -- the route segment comes from `[Resource]`.

**How to fix**: add `[Resource("reservations")]` to the parent entity.


#### PRAG2602 / PRAG2603 / PRAG2605 -- Resource segments

- **PRAG2602**: the `[Resource]` segment must be lowercase kebab-case (`reservations`, `room-types`).
- **PRAG2603**: two entities in one boundary claim the same segment.
- **PRAG2605**: capabilities were declared without `Read`, so `GET /{segment}/{id}` is not generated.

---

### 3.18 Source generator infrastructure (PRAG9000)

#### PRAG9000 -- Source generator output failed

**What it means**: one generator output threw. The code it should have produced is missing, so the build
will also show unresolved-type errors that point at the symptom rather than the cause. Other outputs are
unaffected.

**How to fix**: this is a generator bug. Report it with the triggering code.

---

## 4. Common Troubleshooting Patterns

### 4.1 "My SG Is Not Generating Code"

Use this decision tree when you expect generated code but nothing appears:

1. **Is the class `partial`?**
   Almost every Pragmatic attribute requires the target class to be `partial`. Check for
   PRAG0200/0300/0400/0406/0500/0600/0602/0801/1100/1700/2000/2200/2502.

2. **Does the class inherit from the correct base?**
   - Actions: `DomainAction<T>` or `VoidDomainAction`
   - Mutations: `Mutation<TEntity>`
   - Endpoints: `Endpoint<T>` or `VoidEndpoint`

3. **Are the NuGet references correct?**
   Run `dotnet build` with verbosity set to `diagnostic` to see which SG pipelines are active:
   ```bash
   dotnet build -v diag 2>&1 | grep "Pragmatic"
   ```

4. **Is FeatureDetector finding the module?**
   Check for PRAG1690/1691 Info diagnostics. If no modules are discovered, ensure your library
   project references the correct Pragmatic runtime package.

5. **Clean and rebuild.**
   SG output is cached aggressively. Force a clean rebuild:
   ```bash
   dotnet clean && dotnet build
   ```

6. **Check for SG errors in build output.**
   The SG may silently skip generation when it encounters an error. Look for `PRAG` entries
   in the Warning/Error list.

### 4.2 "I Get a PRAG Error but My Code Looks Correct"

Checklist:

- [ ] **Rebuild the solution.** Stale SG state can report phantom errors.
- [ ] **Check namespace.** Attributes must be from Pragmatic namespaces, not similarly named third-party ones.
- [ ] **Check using directives.** Ensure you are importing the correct namespace for the attribute.
- [ ] **Check target framework.** Some features require specific .NET versions (e.g., PRAG1646 for keyed services).
- [ ] **Check attribute spelling.** Generic attributes (`[Entity]`) vs non-generic (`[Entity(typeof(Guid))]`).
  Pragmatic exclusively uses generic attributes.
- [ ] **Verify package versions match.** Mixed versions across Pragmatic packages can cause schema version
  diagnostics (PRAG1610/1611/1612).

### 4.3 "How to Add a New Diagnostic"

For contributors adding diagnostics to the Pragmatic source generator:

1. Determine the module and allocate an ID from the correct range (`docs/diagnostics.md`), then grep the
   whole repo for the candidate ID -- descriptors also live in the standalone analyzers, and nothing
   detects a collision automatically.
2. Create or extend a `*Diagnostics.cs` file in the feature's `Diagnostics/` folder.
3. Use `DiagnosticFactory.Error/Warning/Info()` from `shared/SourceGen/`:
   ```csharp
   public static readonly DiagnosticDescriptor MyNewRule = DiagnosticFactory.Error(
       "PRAG0XXX",
       "Short title",
       "Message with '{0}' placeholder for context",
       "Longer description with fix guidance.");
   ```
4. Report the diagnostic in the Transform or Feature using `context.ReportDiagnostic(descriptor, location, args)`.
5. Add a test in the module's test project that verifies the diagnostic is emitted:
   ```csharp
   [Fact]
   public void MyRule_InvalidInput_EmitsDiagnostic()
   {
       var source = "/* invalid code triggering the rule */";
       var result = RunGenerator(source);
       HasDiagnostic(result, "PRAG0XXX").Should().BeTrue();
   }
   ```
6. A descriptor nothing reports is a promise the build does not keep. Emit it, or do not declare it.
7. Update this troubleshooting guide, `site/docs/src/content/docs/reference/diagnostics.md`, and the
   owning module's `docs/`.

---

## Appendix: ID Range Allocation

| Range | Module | File |
|-------|--------|------|
| PRAG0001--0099 | Result | `Pragmatic.Result.Analyzers/DiagnosticDescriptors.cs` |
| PRAG0100--0199 | Ensure | reserved -- nothing emitted today |
| PRAG0200--0299 | Validation | `ValidationDiagnostics.cs` |
| PRAG0300--0399 | Mapping | `MappingDiagnostics.cs` |
| PRAG0400--0449 | Actions | `ActionsDiagnostics.cs` |
| PRAG0500--0599 | Endpoints | `EndpointsDiagnostics.cs` |
| PRAG0600--0699 | Persistence + EF Core | `PersistenceDiagnostics.cs`; 0600/0602 in `SourceGenerator.Analyzers/NotPartialDiagnosticDescriptors.cs`; 0680--0686 in `Pragmatic.Persistence.Analyzers/DiagnosticDescriptors.cs` |
| PRAG0700--0799 | Query Pipeline | `QueryPipelineDiagnostics.cs` |
| PRAG0800--0899 | Messaging | `MessagingDiagnostics.cs`; 0822 in `SourceGenerator.Analyzers/EventGraphAnalyzerDescriptors.cs` |
| PRAG0900--0999 | Temporal | `Pragmatic.Temporal.Analyzers/DiagnosticDescriptors.cs` (0900--0904); `TemporalDiagnostics.cs` (0905) |
| PRAG1000--1099 | Identity / Authorization | `IdentityDiagnostics.cs` (1050 is Composition's `DuplicateUsePackage` -- a range trespass) |
| PRAG1100--1199 | Data Ownership | `OwnershipDiagnostics.cs`; 1100 in `SourceGenerator.Analyzers/NotPartialDiagnosticDescriptors.cs` |
| PRAG1400--1499 | Dependency injection | `Pragmatic.Abstractions.Analyzers/DiagnosticDescriptors.cs` |
| PRAG1600--1699 | Composition | `CompositionDiagnostics.cs` |
| PRAG1700--1799 | Caching | `CachingDiagnostics.cs` |
| PRAG1800--1899 | Internationalization | `I18NDiagnostics.cs` |
| PRAG1900--1999 | Documents | `Pragmatic.Documents.Csv.Generator/Diagnostics/CsvDiagnostics.cs` |
| PRAG2000--2099 | Configuration | `ConfigurationDiagnostics.cs` |
| PRAG2200--2249 | Patch | `PatchDiagnostics.cs` |
| PRAG2500--2549 | Jobs | `JobsDiagnostics.cs` |
| PRAG2600--2699 | Traits + Resource (shared) | `TraitDiagnostics.cs`, `ResourceDiagnostics.cs` |
| PRAG2700--2749 | Value objects | `ValueObjectDiagnostics.cs` |
| PRAG2750--2799 | Entity lifecycle events | `LifecycleEventsDiagnostics.cs` |
| PRAG2800--2899 | Serialization / AOT | `Pragmatic.Abstractions.Analyzers/DiagnosticDescriptors.cs` |
| PRAG9000--9099 | SG infrastructure | `shared/SourceGen/SafeSourceOutput.cs`; 9001 inline in `Features/Manifest/ManifestFeature.cs` |
| PRAGS001--S999 | Suppressors | `Features/../Suppressors/` (`SuppressionDescriptor`, not `DiagnosticDescriptor`) |
