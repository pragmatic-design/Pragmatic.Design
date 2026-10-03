# Cookbook — CRUD Web API

End-to-end recipe for a Pragmatic.Design app from scratch, consuming NuGet packages (no source access).
Result: HTTP CRUD API on PostgreSQL with entity, repository, mutation, query, custom action, and
Result→ProblemDetails error mapping.

> Every step here was validated by building and running this exact shape end-to-end against the
> `1.0.0-alpha` packages. The notes marked **⚠ required** are non-obvious things that, if skipped,
> produce a build error or a startup/runtime failure.

Build in order. Run `dotnet build` after each main step.

## 0. Prerequisites

- .NET 10 SDK, local PostgreSQL (or `docker run -p 5432:5432 -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=myapp postgres:17`).
- NuGet feed: the packages are on nuget.org as prereleases; see `../nuget-feed.md` only to consume unreleased builds from a local BaGetter.

## 1. Project layout

```
MyApp/
├── NuGet.config
├── src/
│   ├── MyApp.Sales/                    # boundary library
│   │   ├── MyApp.Sales.csproj
│   │   ├── SalesBoundary.cs
│   │   ├── SalesModule.cs
│   │   └── Orders/
│   │       ├── Order.cs                # entity  → namespace MyApp.Sales.Entities (⚠ see step 5)
│   │       ├── Dtos/OrderDto.cs
│   │       ├── Mutations/CreateOrderMutation.cs
│   │       ├── Queries/SearchOrdersQuery.cs
│   │       └── Actions/AdjustTotalAction.cs
│   └── MyApp.Host/                     # web host
│       ├── MyApp.Host.csproj
│       ├── Program.cs
│       ├── AppDatabase.cs
│       ├── HostModule.cs               # ⚠ host topology (see step 9)
│       └── appsettings.json
```

## 2. NuGet.config (repo root)

Not needed with the packages from nuget.org. Required only if you use a local BaGetter for builds
that are not released yet.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-bagetter" value="http://localhost:5555/v3/index.json" allowInsecureConnections="true" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-bagetter"><package pattern="Pragmatic.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

## 3. Boundary library — `MyApp.Sales.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Pragmatic.Abstractions" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Result" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Ensure" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Validation" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Mapping" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Actions" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Endpoints" Version="1.0.0-alpha.1" />
    <!-- ⚠ required: [Endpoint] generates Microsoft.AspNetCore.* code INTO this assembly -->
    <PackageReference Include="Pragmatic.Endpoints.AspNetCore" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Persistence" Version="1.0.0-alpha.1" />
    <!-- ⚠ required: entity infrastructure (IEntity.PersistenceId) is generated here, needs EFCore types -->
    <PackageReference Include="Pragmatic.Persistence.EFCore" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
      <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

## 4. Boundary + Module (marker types)

```csharp
// SalesBoundary.cs
using Pragmatic.Actions.Attributes;
namespace MyApp.Sales;

[Boundary]
public partial class SalesBoundary;
```

```csharp
// SalesModule.cs
using Pragmatic.Composition.Attributes;   // ModuleAttribute lives in Pragmatic.Abstractions
namespace MyApp.Sales;

[Module(Name = "MyApp.Sales", Version = "1.0.0", Description = "Sales")]
public sealed class SalesModule;
```

Everything under `MyApp.Sales.*` is captured by the boundary via namespace prefix. Sub-folders
(`Orders/`, `Customers/`, …) become inferred sub-boundaries.

## 5. Entity — `Orders/Order.cs`

> ⚠ **required: entities must live in the `{Boundary}.Entities` namespace** (here
> `MyApp.Sales.Entities`), regardless of folder. The source generator derives the host database's
> root namespace from the entity namespace; a feature-folder namespace such as `MyApp.Sales.Orders`
> makes the generated migration/schema types land in the wrong namespace and the host won't compile.

```csharp
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Attributes;     // [Projectable]
using System.ComponentModel.DataAnnotations;

namespace MyApp.Sales.Entities;                    // ⚠ .Entities, not .Orders

[Entity]                          // PersistenceId: Guid (Guid7), Id as its alias
[Auditable]                       // CreatedAt/By, UpdatedAt/By
[SoftDelete]                      // IsDeleted + global filter
[BelongsTo<SalesBoundary>]
public partial class Order : IEntity
{
    [LogicKey]                                          // → Order.Repository.GetByOrderNumberAsync(...)
    [Required]
    public string OrderNumber { get; private set; } = "";

    [Required]
    public Guid CustomerId { get; private set; }

    [Range(0.01, double.MaxValue)]
    public decimal Total { get; private set; }

    [Projectable]                                       // SQL-translatable
    public bool IsHighValue => Total >= 1000m;

    /// <summary>Domain mutation — properties have private setters; expose intent as methods.</summary>
    public void AdjustTotal(decimal delta) => Total = Math.Max(0m, Total + delta);
}
```

The SG generates `Order.Create(customerId, total)`, `Id`/`PersistenceId`, audit + soft-delete
members, and the nested `Order.Repository` (an `IRepository<Order>`) with a `GetByOrderNumberAsync`
lookup — on the concrete type, not on the interface.

⚠ `OrderNumber` is **not** a factory parameter: it has an initializer. A property becomes one only
when it is non-nullable, has no initializer and is not a foreign key — see
`domain-model.md` §4. Set the others with the generated `SetOrderNumber(...)`.

> Auto-generated human-readable keys (`ORD-202606-00001`) use `[GeneratedValue("ORD-{YYYY}{MM}-{SEQ:5}")]`
> alone: the unit of work fills the value before the save (`{SEQ}` from a database sequence), so the
> mutation does not carry it and a `[Raises<T>]` event built after the save sees it. Omitted here for
> brevity.

## 6. Mutation — `Orders/Mutations/CreateOrderMutation.cs`

```csharp
using Pragmatic.Actions.Mutation;                 // Mutation<T>, [Mutation], MutationMode
using Pragmatic.Endpoints;                         // HttpVerb
using Pragmatic.Endpoints.Attributes;              // [Endpoint]
using MyApp.Sales.Entities;

namespace MyApp.Sales.Orders.Mutations;

// Init properties are mapped by name onto the generated Order.Create(...) factory.
// No version segment in the route: see the note under §7 before writing "api/v1/…".
[Mutation(Mode = MutationMode.Create)]
[Endpoint(HttpVerb.Post, "api/orders")]
public partial class CreateOrderMutation : Mutation<Order>
{
    public required string OrderNumber { get; init; }
    public required Guid CustomerId { get; init; }
    public required decimal Total { get; init; }
}
```

The SG produces the `CreateOrderMutation.Invoker` (DI resolution, validation, transaction commit) and
the endpoint registration. The endpoint answers HTTP 201 with `{"id": "…"}`: a mutation that declares no
answer never puts the entity on the wire, and an update or a delete that declares none answers 204.
`[ReturnsDto<OrderDto>]` answers that DTO instead, `LogicalKey` answers the entity's `[LogicKey]`, and
`ReturnType = MutationReturnType.Entity`, written explicitly, answers the entity itself. In process the
boundary member returns the entity either way. Once the read of step 7-bis
exists at `api/orders/{id}`, the 201 also carries `Location: /api/orders/{id}`.

## 7. Query — `Orders/Queries/SearchOrdersQuery.cs`

```csharp
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query;                 // FilterOperator, SortDirection
using Pragmatic.Persistence.Query.Attributes;      // [Filter], [Sort]
using MyApp.Sales.Dtos;
using MyApp.Sales.Entities;

namespace MyApp.Sales.Orders.Queries;

[Query<Order, OrderDto>(Paged = true)]             // Paged = true writes Page/PageSize
[Endpoint(HttpVerb.Get, "api/orders")]
public partial class SearchOrdersQuery
{
    public Guid? CustomerId { get; init; }

    [Filter(Operator = FilterOperator.Contains)]
    public string? OrderNumber { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "Total")]
    public decimal? MinTotal { get; init; }

    [Sort(MapTo = "OrderNumber", DefaultDirection = SortDirection.Ascending)]
    public SortDirection? OrderNumberSort { get; init; }
}
```

`Paged = true` generates `Page = 1` and `PageSize = 20`. Write them by hand only to change the
defaults — declaring both the option and the properties is **PRAG0727**.

⚠️ **When the rule already exists as a `Specification<T>`, this class is the long way round.**
`[Query]` on the static member derives the query from it; the class form is for a read that composes
several filters, a sort and a projection, which is what this one does.

The projection DTO **must be a `partial class` with `[MapFrom<TEntity>]` and `[GenerateProjection]`**
— this is what produces the SQL-translatable projection the query pipeline consumes. A plain record
with matching names is **not** enough.

```csharp
// Orders/Dtos/OrderDto.cs
using Pragmatic.Mapping.Attributes;                // [MapFrom], [GenerateProjection]
using MyApp.Sales.Entities;

namespace MyApp.Sales.Dtos;

[MapFrom<Order>]
[GenerateProjection]
public partial class OrderDto
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public Guid CustomerId { get; init; }
    public decimal Total { get; init; }
    public bool IsHighValue { get; init; }
}
```

The query endpoint returns a paged result (`items`, `totalCount`, `page`, `pageSize`, `totalPages`).

### Do not hand-write a version into the route

`"api/v1/orders"` looks like versioning and is not: it is a literal that happens to contain a `v1`.
When a second version arrives you own two unrelated routes and nothing relates them.

Leave the segment out. If the API genuinely needs versions, that is a framework feature — add the
`Asp.Versioning.Http` package and declare it:

```csharp
[ApiVersion("2.0")]
[Endpoint(HttpVerb.Get, "api/orders")]        // served at /api/v2/orders
```

The generator emits the `ApiVersionSet` and `MapToApiVersion` wiring, and the host calls
`AddApiVersioning()` on its own. Without the package the generator says so with a diagnostic rather
than silently producing an unversioned route.

### A DTO that nests

Nesting works, including a collection of nested DTOs, and the whole tree is inlined into **one**
query. The rule is that **every level carries its own `[MapFrom<T>]`**, and every level is `partial`:

```csharp
[MapFrom<Order>] [GenerateProjection]
public partial class OrderCardDto
{
    public Guid Id { get; init; }
    public IReadOnlyList<OrderLineCardDto> Lines { get; init; } = [];   // the navigation on Order
}

[MapFrom<OrderLine>]                    // ← required. Without it the whole branch is dropped
public partial class OrderLineCardDto
{
    public string Sku { get; init; } = "";
}
```

⚠️ A nested level without `[MapFrom]` is **not** an error and not a warning: the property is skipped
and the payload comes back with an empty list. Three records in one file with the attribute only on
the outer one is the shape that produces it, and it looks right.

The navigation the nested type maps to may be one the generator writes — `[Relation.OneToMany<T>]`
produces `Ts`, `[Relation.ManyToOne<T>]` produces `T` — and projecting over it works: the generators
know what each other will emit, so the nested `[MapFrom]` is the only thing you owe.

A detail read usually wants three more shapes, and all three stay in the one query:

```csharp
[MapFrom<LeaveRequest>] [GenerateProjection]
public partial class LeaveRequestDetailDto
{
    public EmployeeReferenceDto Employee { get; init; } = null!;   // required relation: no null branch

    [MapCondition(nameof(IsDecided))]
    public LeaveDecisionDetailDto? Decision { get; init; }         // present only once decided

    private static bool IsDecided(LeaveRequest request) => request.Status != LeaveRequestStatus.Pending;
}

[MapFrom<LeaveRequest>]                 // the SAME entity: a group of columns of the same row
public partial class LeaveDecisionDetailDto
{
    public EmployeeReferenceDto DecidedBy { get; init; } = null!;
    public DateTimeOffset DecidedAt { get; init; }                 // nullable column → default when absent
}
```

- A nested DTO whose `[MapFrom]` names the container's own entity is built from the row — no
  navigation, no join.
- A nested reference declared **non-nullable** gets no null check in the projection; in `FromEntity`
  an unloaded navigation behind it throws, naming the property, rather than handing back a `null`.
- `[MapCondition]` gates the projection too when the predicate has an **expression body** — it becomes
  `(predicate) ? mapping : default!`. With a block body the projection maps unconditionally and
  `PRAG0332` says so.
- A localized name inside a nested DTO is read as its value. A nested member the projection cannot
  build keeps the DTO's default and is reported (`PRAG0326`).

### Flattening instead of nesting

When you want a few fields from further down rather than a nested object, give the path:

```csharp
[MapFrom<Assignment>] [GenerateProjection]
public partial class MyAssignmentDto
{
    [MapProperty("WorkItem.Description")]        public string WorkItemDescription { get; init; } = "";
    [MapProperty("WorkItem.Worksite.Address")]   public string WorksiteAddress { get; init; } = "";
}
```

The whole path is inlined into the SQL — including segments that are generated navigations, and
however many levels deep. This replaces a hand-written `.Select(...)`, which is what most people
write here because the recipe never showed the alternative.

The nested type maps to a **navigation**, so it cannot cross a boundary (**PRAG0334**) — across a
boundary you hold an id, and the second read comes from that boundary's `[Published]` query (see
`domain-model.md` §5). Give it a batch form: one call for the whole page, not one per row.

## 7-bis. Read one — `Orders/Queries/GetOrderQuery.cs`

A get-by-id is the same query with one filter, no paging, and `Single = true`. Do **not** hand-write
an `Endpoint<T>` that injects a repository and returns `NotFoundError` for this: that is what the
declarative form replaces.

```csharp
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Query.Attributes;      // [Query]
using MyApp.Sales.Dtos;
using MyApp.Sales.Entities;

namespace MyApp.Sales.Orders.Queries;

[Query<Order, OrderDto>(Single = true)]
[Endpoint(HttpVerb.Get, "api/orders/{id}")]
public partial class GetOrderQuery
{
    public required Guid Id { get; init; }         // the same line an update mutation carries
}
```

The endpoint returns the DTO itself instead of a list, and answers **404** when nothing matches. The
executor takes the first row (`FirstOrDefault`, not `Single`), so a filter that is not unique is not
an error. The entity's `Id` is an alias of `PersistenceId`, and a filter on it targets the key without
being told.

## 8. Custom action — `Orders/Actions/AdjustTotalAction.cs`

```csharp
using Pragmatic.Actions.Abstractions;              // DomainAction<T>
using Pragmatic.Actions.Attributes;                // [DomainAction], [LoadEntity<T>]
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;                            // Result, IError
using Pragmatic.Result.Http;                       // NotFoundError
using MyApp.Sales.Entities;

namespace MyApp.Sales.Orders.Actions;

[DomainAction]
[LoadEntity<Order>(nameof(OrderId))]              // loads _order, or answers 404 before Execute
[Endpoint(HttpVerb.Post, "api/orders/adjust-total")]
public partial class AdjustTotalAction : DomainAction<Guid, NotFoundError>   // ← declare the errors
{
    public required Guid OrderId { get; init; }
    public required decimal Delta { get; init; }

    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        _order.AdjustTotal(Delta);                    // tracked: committed by the invoker
        return Task.FromResult<Result<Guid, IError>>(_order.Id);
    }
}
```

**An entity named by a key the operation carries is `[LoadEntity<T>]`, not a repository call.** The
invoker loads it into a generated field (`_order`), answers 404 when the key names nothing — after
validation and authorization, past a `[WithoutFilter<T>]` the operation declares — and `Execute` never
sees it missing. What `Execute` must not hold, and where each part goes instead (validation, async
validators, named specifications), is listed in `pragmatic-use-actions-endpoints` → *What does not go in
`Execute`*.

**Declare the errors on the base.** `DomainAction<TReturn, TError1…>` exists up to six errors (and
`VoidDomainAction<TError1…>` likewise). The plain `DomainAction<Guid>` returns
`Result<Guid, IError>`, which says nothing — and what the base does not declare, the generator cannot
emit: no `ProducesProblem`, no 422 in OpenAPI, nothing in the client manifest. Your API then does not
document the errors it returns.

`IRepository<TEntity>` takes **one** type argument, the entity. `NotFoundError` is in
`Pragmatic.Result.Http`; construct it with `NotFoundError.Create(type, id)` / `.For(type, id)`.

## 9. Host — `MyApp.Host.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyApp.Sales\MyApp.Sales.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Pragmatic.Composition.Host" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Endpoints.AspNetCore" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Result.AspNetCore" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Persistence.EFCore" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Migrations" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Internationalization.AspNetCore" Version="1.0.0-alpha.1" />
    <!-- ⚠ required: who calls. Users → this package; deliberately none → [AnonymousHost] instead.
         With neither, the build stops on PRAG1695. -->
    <PackageReference Include="Pragmatic.Identity.AspNetCore" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.Endpoints.OpenApi" Version="1.0.0-alpha.1" />
    <PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
      <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <!-- ⚠ required: EFCore bundles no provider; match Pragmatic's EF Core major (.NET 10 → EF Core 10) -->
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.*" />
  </ItemGroup>
</Project>
```

> With `Pragmatic.Endpoints.OpenApi` (above) the generated host serves `/openapi/v1.json` in
> Development, and Scalar over it at `/scalar` when `Scalar.AspNetCore` is referenced — no startup step.
> Other environments publish the document after `app.UseApiDocumentation()`. Nothing else is required
> on the host: caching, resilience, jobs and messaging are wired when a module declares them.

## 10. Database marker — `AppDatabase.cs`

```csharp
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace MyApp.Host;

[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

## 11. Host topology — `HostModule.cs`  (⚠ required, easy to miss)

The host must declare **which module goes into which database** via a `[Module]` class with
`[Include<TModule, TDatabase>]`. Without this the host logs *"No databases configured"*, the DbContext
and `IUnitOfWork` are never registered, and startup fails DI validation.

```csharp
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;                          // RoutingStep
using Pragmatic.Internationalization.AspNetCore.Steps;      // InternationalizationStep
using MyApp.Sales;

namespace MyApp.Host;

[Module]
[Include<SalesModule, AppDatabase>]
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class HostModule;
```

## 12. Program.cs

```csharp
using Pragmatic.Composition.Hosting;
using Pragmatic.Identity;                          // UseDevelopmentIdentity
using Pragmatic.Internationalization;
using Pragmatic.Internationalization.Types;        // CultureCode
using Pragmatic.Migrations.Extensions;

await PragmaticApp.RunAsync(args, app =>
{
    // ⚠ A culture has to come from somewhere, or the host refuses to start: from the modules'
    // translations/{culture}.json (the one they are written from), or from here — which also wins over them.
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS);
    });

    // Declarative schema diff: creates/updates tables from the decorated [Entity] types.
    app.UsePragmaticMigrations();

    // ⚠ required unless the host is [AnonymousHost]: who is calling (§14-ter).
    if (app.Environment.IsDevelopment())
        app.UseDevelopmentIdentity();

    // Add more Use*() here for Jobs, Messaging, Notifications when needed.
}).ConfigureAwait(false);
```

## 13. appsettings.json

```json
{
  "ConnectionStrings": {
    "App": "Host=localhost;Database=myapp;Username=postgres;Password=dev"
  }
}
```

## 14. Build & run

```powershell
dotnet build
dotnet run --project src/MyApp.Host
```

Available endpoints:

- `POST /api/orders` — create order → `201` with the created entity.
- `GET  /api/orders?orderNumber=...&minTotal=...&page=1&pageSize=20` — paged search → `200`.
- `POST /api/orders/adjust-total` — custom action → `201`, or `404` (`NOT_FOUND`) if the id is unknown.

## 14-ter. Running locally without an identity provider

One call, in the `PragmaticApp.RunAsync` callback:

```csharp
if (app.Environment.IsDevelopment())
    app.UseDevelopmentIdentity();          // Pragmatic.Identity.AspNetCore
else
    app.UseJwtAuthentication(jwt => { /* … */ });
```

It registers the no-op scheme, the authentication pipeline step, and the step that puts
`HeaderUserMiddleware` **ahead** of the authentication middleware — so the `X-User-*` headers become
the current identity. Outside Development the handler throws, which is deliberate: a production host
must configure a real scheme.

⚠️ Do not assemble those three by hand. The order matters (the principal has to exist before
authorization looks at it) and there is a trap on the way: an `IStartupStep`'s `ConfigureServices`
and `ConfigurePipeline` run on **different instances**, so a flag set in the first is gone in the
second — resolve `IHostEnvironment` from `app.ApplicationServices` instead.

## 14-bis. Calling a protected endpoint from a test

Do **not** hand-roll a token minter. `Pragmatic.Testing` ships the identity headers a Pragmatic host
trusts, and `AsUser` sets them for every request the client sends:

```csharp
using Pragmatic.Testing;                          // PragmaticTestIdentity.AsUser

client.AsUser("u-1", tenantId: "acme", userName: "Ada", roles: ["sales-manager"]);
```

It sends `X-User-Id`, `X-Tenant-Id`, `X-User-Name`, and whichever of `X-User-Roles`,
`X-User-Groups`, `X-User-Permissions` you supply.

⚠️ **Drive the roles, not the permissions.** Once the host calls `UseAuthorization(authz => …)`,
`IUserAuthorization` becomes a resolver built from the registered `IPermissionProvider`s, and the raw
`permission` claim the header carries is no longer what the gate asks: an exact
`X-User-Permissions: suppliers.supplier.read` gets a 403 while the role that grants it through
`MapRole` gets a 200. Authenticating by role is also closer to the real configuration.

`permissions:` still exists and is right for a host that has no role map — and for the deliberately
underprivileged caller, where passing none writes an *empty* permission header on a single request so
the client's default grant cannot leak through.

⚠️ **Those headers are honoured only while bearer auth is off** (the dev no-op handler). Once the
host calls `UseJwtAuthentication`, they are ignored and the request is anonymous. For a host with
real JWT, mint a token with the same generator the host validates against, so the claims match by
construction:

```csharp
var generator = new JwtTokenGenerator(Options.Create(new JwtOptions
{
    SigningKey = "<the host's key>", Issuer = "myapp", Audience = "myapp-api",
    // A token minted outside an identity store carries no "sstamp":
    RequireSecurityStamp = false,        // set on the HOST's options too, or every call is 401
}));

var token = generator.Generate("u-1", "Ada", tenantId: "acme", roles: ["staff"]);   // AccessToken
client.DefaultRequestHeaders.Authorization = new("Bearer", token.Token);
```

## 15. Inspect generated code

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

After `dotnet build`, read `obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/...`.

⚠ **Do not redirect it with `CompilerGeneratedFilesOutputPath` in `Directory.Build.props`.** That file
is imported before the SDK defines `$(BaseIntermediateOutputPath)`, so the obvious
`$(BaseIntermediateOutputPath)generated` resolves to `generated/` at the project root — inside the
default `**/*.cs` glob. Every generated type is then compiled twice, and the build reports dozens of
duplicate-member errors that name members, never the folder. Leave the default, or write a path under
`obj/` literally: `$(MSBuildProjectDirectory)/obj/generated`.

## 16. Common troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Microsoft.AspNetCore` namespace missing in generated `*.Endpoint.g.cs` | boundary lib lacks `Pragmatic.Endpoints.AspNetCore` | add it (step 3) |
| CS0535 `IEntity.PersistenceId` not implemented | boundary lib lacks `Pragmatic.Persistence.EFCore` | add it (step 3) |
| `'OrderDto' does not contain 'Projection'` | DTO is a plain record / no `[GenerateProjection]` | partial class + `[MapFrom<T>]` + `[GenerateProjection]` (step 7) |
| Generated migration/schema types in wrong namespace (CS0234 on `*.MigrationDbContext`) | entity not in `{Boundary}.Entities` namespace | move entity namespace (step 5) |
| Startup: *No databases configured* / `IUnitOfWork` unresolved | missing `[Include<TModule,TDatabase>]` host module | add `HostModule` (step 11) |
| Build: **PRAG1695** *Pragmatic.Authorization is referenced … but Pragmatic.Identity is not* | the host made no identity choice | `Pragmatic.Identity.AspNetCore` + an authentication method, or `[AnonymousHost]` on `HostModule` (step 9) |
| Startup: *The endpoints require authorization and no authentication method is configured for environment Production* (the maintenance page, 503, unless disabled) | no authentication method outside Development (`UseDevelopmentIdentity` is Development only) | run in Development (`launchSettings.json`), or configure a real scheme (§14-ter) |
| `/openapi/v1.json` is 404 | the host does not reference `Pragmatic.Endpoints.OpenApi`, or runs outside Development | reference the package (step 9); outside Development call `app.UseApiDocumentation()` |
| Startup: `I18NConfigurationException` *No UI culture configured* | no culture from `UseI18N` nor from the modules' `translations/` | add `UseI18N` with a default (step 12), or a translation file (a provider answering per request defers the check to the request) |
| `MissingMethodException` in Npgsql at first model build | EF provider major ≠ Pragmatic EF Core major | use `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.*` (step 9) |
| 404 on every endpoint | boundary not referenced by host, or `[Boundary]`/`[Module]` missing | verify markers + ProjectReference |

## 17. Natural extensions

- **Auth**: `Pragmatic.Identity` + `Pragmatic.Authorization` → `app.UseJwtAuthentication(...)`, `[RequirePermission(...)]`.
- **Background**: `Pragmatic.Jobs` → `[Job]`, `[RecurringJob]`, `app.UseJobs(...)`.
- **Outbox/Events**: `Pragmatic.Messaging` (+ `Pragmatic.Messaging.EFCore`).
- **Soft permissions**: `[HasOwner]` on the entity → each user sees only their own rows.
- **Partial update**: `[Patch<Order>]` on an `UpdateOrderPatch` with nullable properties.

For each: see the corresponding `pragmatic-use-{name}` skill.
