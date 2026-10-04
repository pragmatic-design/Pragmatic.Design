# Consumer Package Setup

Use these package groups when building an app without Pragmatic source access.

## Minimal Foundation

```xml
<PackageReference Include="Pragmatic.Result" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Ensure" Version="1.0.0-alpha.1" />
```

## Source Generator Analyzer

Use for packages that rely on generated code:

```xml
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Some packages may bring a dedicated or transitive generator. If generated members are missing, add the analyzer explicitly and rebuild.

## CRUD Web API Stack

Split into a **boundary library** and a **web host** (different package sets). Full validated recipe:
`cookbook/crud-web-api.md`.

**Boundary library** (entities, mutations, queries, actions):

```xml
<PackageReference Include="Pragmatic.Abstractions" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Result" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Ensure" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Validation" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Mapping" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Actions" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Endpoints" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Endpoints.AspNetCore" Version="1.0.0-alpha.1" />  <!-- required for [Endpoint] -->
<PackageReference Include="Pragmatic.Persistence" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Persistence.EFCore" Version="1.0.0-alpha.1" />     <!-- required for entity infra -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

**Web host** (SDK `Microsoft.NET.Sdk.Web`):

```xml
<PackageReference Include="Pragmatic.Composition.Host" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Endpoints.AspNetCore" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Result.AspNetCore" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Persistence.EFCore" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Migrations" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Internationalization.AspNetCore" Version="1.0.0-alpha.1" />  <!-- UseI18N -->
<!-- who calls: users → Identity.AspNetCore; deliberately none → [AnonymousHost] instead (else PRAG1695) -->
<PackageReference Include="Pragmatic.Identity.AspNetCore" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Endpoints.OpenApi" Version="1.0.0-alpha.1" />  <!-- /openapi/v1.json -->
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
<!-- EF provider, matching Pragmatic's EF Core major (.NET 10 → EF Core 10) -->
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.*" />
```

Nothing else is required: caching, resilience, jobs and messaging are wired when a module declares
them, not because their package is on the host.

Add only when needed:

| Need | Package |
|---|---|
| DTO mapping / projection | `Pragmatic.Mapping` |
| Partial update DTOs | `Pragmatic.Patch` |
| Roles and permissions beyond "authenticated" | `Pragmatic.Authorization` is already there through `Pragmatic.Actions`; a real scheme (`Pragmatic.Identity.Local.Jwt`, `Pragmatic.Identity.Oidc`) replaces the development identity |
| Multi-tenancy (host) | `Pragmatic.MultiTenancy.AspNetCore` |
| Async bus / outbox / saga | `Pragmatic.Messaging` |
| Background work | `Pragmatic.Jobs` |
| Cache keys / invalidation | `Pragmatic.Caching` |
| Clock / dates / cron | `Pragmatic.Temporal` |
| i18n / money / currencies | `Pragmatic.Internationalization` |

## Consumer Project Defaults

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

With `ImplicitUsings` on, every Pragmatic package the project references **directly** adds the namespaces you write against with it (`Pragmatic.Actions` → `.Abstractions`, `.Attributes`, `.Mutation`; `Pragmatic.Endpoints` → its attributes and base types; `Pragmatic.Abstractions` → `Pragmatic.Result`, `Pragmatic.Authorization`, `Pragmatic.Persistence.Entity`, …). Do not write `using` lines for those. A namespace from a package referenced only transitively still needs its `using`, or a direct reference. `<PragmaticImplicitUsings>false</PragmaticImplicitUsings>` switches them all off. `Pragmatic.Composition.Attributes` (`[Service]`, `[Module]`, `[Include]`, `Lifetime`) is imported like the others (the lifetime enum is `Lifetime`, not `ServiceLifetime`, so it does not collide with the one the Web SDK imports). A module's own flat namespaces (`{Module}.Entities`, `.Dtos`, `.Enums`) go in a `GlobalUsings.cs` of the module.
