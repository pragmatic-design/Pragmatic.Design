# Solution scaffold template

The complete best-practice structure for a new Pragmatic.Design consumer app. Lay these files down
first, then fill domain code per `../cookbook/crud-web-api.md`.

⚠️ **Where these files come from, so the claim is checkable.** The test fixture and the web factory are
the ones two reference applications build and run on every gate,
`examples/time-off/tests/TimeOff.IntegrationTests/Infrastructure/` and
`examples/invoicing/tests/Invoicing.IntegrationTests/Infrastructure/`, with their names replaced by the
placeholders. The rest is the structure those two applications have.

Placeholders: `{{App}}` (e.g. `Contoso`), `{{Boundary}}` (e.g. `Students`), a boundary's module is
`{{App}}.{{Boundary}}`. Repeat the boundary library per bounded context.

## Layout

```
{{App}}/
├── global.json
├── Directory.Build.props
├── Directory.Packages.props          # Central Package Management (CPM)
├── NuGet.config
├── .gitignore
├── .editorconfig
├── {{App}}.slnx
├── src/
│   ├── {{App}}.{{Boundary}}/         # one boundary library per bounded context
│   │   ├── {{App}}.{{Boundary}}.csproj
│   │   ├── {{Boundary}}Boundary.cs
│   │   ├── {{Boundary}}Module.cs
│   │   └── {{Feature}}/              # Entities in {{App}}.{{Boundary}}.Entities namespace!
│   └── {{App}}.Host/
│       ├── {{App}}.Host.csproj
│       ├── Program.cs
│       ├── AppDatabase.cs
│       ├── HostModule.cs
│       ├── Properties/launchSettings.json
│       └── appsettings.json
└── tests/{{App}}.IntegrationTests/
    ├── {{App}}.IntegrationTests.csproj
    ├── PostgresFixture.cs
    ├── {{App}}WebFactory.cs
    └── ...Tests.cs
```

## global.json

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
}
```

⚠ `latestFeature`, and the band floor rather than the exact build you happen to have. Pinning
`10.0.201` with `latestPatch` does **not** roll forward to an installed `10.0.302`: a patch roll
stays inside its feature band.

## Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- AOT/trimming + generated-code warnings stay warnings while on 1.0.0-alpha. -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>

    <!-- Read the generated code. Do NOT redirect it with CompilerGeneratedFilesOutputPath here:
         $(BaseIntermediateOutputPath) is not defined yet at this point, so the obvious value lands
         the files at the project root, inside the default glob, and every generated type compiles
         twice. -->
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
</Project>
```

### The generator's opt-in switches

Three MSBuild properties change what the generator emits. They are ordinary properties in your own
`Directory.Build.props`:

| Property | What it turns on |
|---|---|
| `PragmaticGenerateJsonContext` | a `JsonSerializerContext` covering this assembly's serializable boundary types: messages, events, job parameters, sagas, mapping DTOs, SSE items, generated request bodies, and action/endpoint response types |
| `PublishAot` | the same, implicitly: wanting AOT is the same as wanting the context |
| `PragmaticAutoDerivePermissions` | permission constants derived from operation names |

Each also has an equivalent assembly attribute (`[assembly: PragmaticGenerateJsonContext]`), which is
the form to use when the generator comes in by `ProjectReference` rather than as a package.

Generated HTTP endpoints publish and run under Native AOT: they are mapped as `RequestDelegate`s, so
nothing depends on ASP.NET's Request Delegate Generator (which cannot see another generator's output).
The standing proof is `examples/aot-smoke/publish-and-smoke-web.ps1`, in both reflection-fallback
modes. ⚠️ The generated **client** SDK (`Pragmatic.Client`) carries a JSON context built from the
manifest, but no smoke publishes it Native AOT yet.

## Directory.Packages.props

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- One exact version for every Pragmatic package: an alpha can change its API, and a float would
         take the next one on the next restore. Upgrade by changing this line. -->
    <PragmaticVersion>1.0.0-alpha.1</PragmaticVersion>
  </PropertyGroup>

  <ItemGroup><!-- Pragmatic.Design (1.0.0-alpha) -->
    <PackageVersion Include="Pragmatic.Abstractions" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Result" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Result.AspNetCore" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Ensure" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Validation" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Mapping" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Actions" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Endpoints" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Endpoints.AspNetCore" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Persistence" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Persistence.EFCore" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Composition.Host" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Migrations" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Internationalization.AspNetCore" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Identity.AspNetCore" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Endpoints.OpenApi" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.SourceGenerator" Version="$(PragmaticVersion)" />
  </ItemGroup>

  <ItemGroup><!-- EF provider: match Pragmatic's EF Core major (.NET 10 -> EF Core 10) -->
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup><!-- API reference UI over the OpenAPI document, Development only -->
    <PackageVersion Include="Scalar.AspNetCore" Version="2.12.52" />
  </ItemGroup>

  <ItemGroup><!-- Test stack -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <!-- 4.15.0 or later: earlier versions bring SSH.NET 2024.1.0 (GHSA-q939-rpr3-3284, NU1903). -->
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageVersion Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>
</Project>
```

## NuGet.config

The packages are on nuget.org, so the solution names that one source and nothing else:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Only to consume a build that is not released yet, add the local feed and map `Pragmatic.*` to it
(see `../nuget-feed.md`):

```xml
  <packageSources>
    <clear />
    <add key="local-bagetter" value="http://localhost:5555/v3/index.json" allowInsecureConnections="true" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-bagetter"><package pattern="Pragmatic.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
```

## .gitignore

```
bin/
obj/
*.user
artifacts/
.idea/
.vs/
.bagetter-data/
*.log
```

## .editorconfig

```ini
root = true

[*]
charset = utf-8
insert_final_newline = true
indent_style = space
trim_trailing_whitespace = true

[*.cs]
indent_size = 4
csharp_style_namespace_declarations = file_scoped:warning

[*.{csproj,props,targets,slnx,json,yml,yaml}]
indent_size = 2
```

## {{App}}.slnx

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/{{App}}.{{Boundary}}/{{App}}.{{Boundary}}.csproj" />
    <Project Path="src/{{App}}.Host/{{App}}.Host.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/{{App}}.IntegrationTests/{{App}}.IntegrationTests.csproj" />
  </Folder>
</Solution>
```

## Boundary library csproj (versionless, CPM)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Pragmatic.Abstractions" />
    <PackageReference Include="Pragmatic.Result" />
    <PackageReference Include="Pragmatic.Ensure" />
    <PackageReference Include="Pragmatic.Validation" />
    <PackageReference Include="Pragmatic.Mapping" />
    <PackageReference Include="Pragmatic.Actions" />
    <PackageReference Include="Pragmatic.Endpoints" />
    <PackageReference Include="Pragmatic.Endpoints.AspNetCore" />   <!-- required for [Endpoint] -->
    <PackageReference Include="Pragmatic.Persistence" />
    <PackageReference Include="Pragmatic.Persistence.EFCore" />     <!-- required for entity infra -->
    <PackageReference Include="Pragmatic.SourceGenerator">
      <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

## Host csproj (versionless, CPM)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <ProjectReference Include="..\{{App}}.{{Boundary}}\{{App}}.{{Boundary}}.csproj" />
    <!-- add a ProjectReference per boundary library -->
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Pragmatic.Composition.Host" />
    <PackageReference Include="Pragmatic.Endpoints.AspNetCore" />
    <PackageReference Include="Pragmatic.Result.AspNetCore" />
    <PackageReference Include="Pragmatic.Persistence.EFCore" />
    <PackageReference Include="Pragmatic.Migrations" />
    <PackageReference Include="Pragmatic.Internationalization.AspNetCore" />  <!-- UseI18N -->
    <!-- Who calls: users → this package; deliberately none → drop it and put [AnonymousHost] on
         HostModule. With neither, the build stops on PRAG1695. -->
    <PackageReference Include="Pragmatic.Identity.AspNetCore" />
    <PackageReference Include="Pragmatic.Endpoints.OpenApi" />               <!-- /openapi/v1.json -->
    <PackageReference Include="Pragmatic.SourceGenerator">
      <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Scalar.AspNetCore" />                          <!-- /scalar, Development -->
  </ItemGroup>
</Project>
```

Nothing else is required. Caching, resilience, jobs, messaging are wired when a module **declares**
them (`[Cacheable]`, `[ResiliencePolicy]`, `[Job]`, `[MessageHandler]`), not because a package is on
the host; see *Optional feature packages* below for what each one adds.

## Host: Program.cs

```csharp
using Pragmatic.Composition.Hosting;
using Pragmatic.Identity;
using Pragmatic.Internationalization;
using Pragmatic.Internationalization.Types;
using Pragmatic.Migrations.Extensions;

await PragmaticApp.RunAsync(args, app =>
{
    // The culture, until the modules have translations/{culture}.json: then they give it (the one they are
    // written from) and these two lines can go. With neither, the host refuses to start.
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS);
    });
    app.UsePragmaticMigrations();

    // Who is calling. In Development, the X-User-* headers; a real scheme (UseJwtAuthentication,
    // UseOidcAuthentication, …) comes with the security pass. Until then, outside Development the host
    // refuses to start: "no authentication method is configured for environment Production". An
    // [AnonymousHost] drops this line and the package.
    if (app.Environment.IsDevelopment())
        app.UseDevelopmentIdentity();
}).ConfigureAwait(false);
```

Do not add `public partial class Program;` for `WebApplicationFactory<Program>`: since .NET 10 the Web SDK
generates it (`PublicProgramSourceGenerator`).

## Host: the API contract  (no code: the generated host publishes it)

With `Pragmatic.Endpoints.OpenApi` referenced, the generated host serves the document the generator
wrote at compile time at `GET /openapi/v1.json` **in Development**, and (when `Scalar.AspNetCore` is
referenced too) Scalar over it at `GET /scalar`. Do not write a startup step for either.

Every other environment is a decision the application states, because a published contract lists every
operation and the permission it requires:

```csharp
app.UseApiDocumentation();   // using Pragmatic.Endpoints.OpenApi: the document in every environment
```

Scalar stays in Development either way. A document or `/scalar` route the application maps itself is
kept, and the generated one steps aside.

## Host: Properties/launchSettings.json

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "{{App}}": {
      "commandName": "Project",
      "launchBrowser": true,
      "launchUrl": "scalar",
      "applicationUrl": "http://localhost:5090",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Without it `dotnet run` starts in **Production**, where the development identity is off and the host
refuses to start ("no authentication method is configured for environment Production"), and the
maintenance page answers 503 unless disabled.

## Host: AppDatabase.cs

```csharp
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace {{App}}.Host;

[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
```

## Host: HostModule.cs  (⚠ required topology, one [Include] per module)

```csharp
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Steps;
using {{App}}.{{Boundary}};

namespace {{App}}.Host;

[Module]
[Include<{{Boundary}}Module, AppDatabase>]      // repeat per boundary module
[NeedsStep<InternationalizationStep>]
[NeedsStep<RoutingStep>]
public sealed class HostModule;
```

## Host: appsettings.json

```json
{
  "ConnectionStrings": { "App": "Host=localhost;Database={{app}};Username=postgres;Password=dev" }
}
```

## Test project: {{App}}.IntegrationTests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\{{App}}.Host\{{App}}.Host.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
</Project>
```

## Test project: PostgresFixture.cs

```csharp
using Testcontainers.PostgreSql;
using Xunit;

namespace {{App}}.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    // WithImage works on every 4.x. The shorter `new PostgreSqlBuilder("postgres:17-alpine")` needs a
    // recent one (4.15.0 has it; 4.2.0 does not: CS1729).
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "integration";
}
```

## Test project: {{App}}WebFactory.cs

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace {{App}}.IntegrationTests;

public sealed class {{App}}WebFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // ⚠️ Read the note under this block before changing this line. "Testing" once the host has a
        // real authentication scheme; "Development" while it still has only UseDevelopmentIdentity(),
        // or the host refuses to start.
        builder.UseEnvironment("Development");

        // UseSetting, not ConfigureAppConfiguration: Program.cs reads configuration **while it
        // registers the services** (a signing key, a connection string, which provider to use), and
        // configuration added by ConfigureAppConfiguration is not visible that early. A host that
        // chooses a service from a setting silently gets the wrong one.
        builder.UseSetting("ConnectionStrings:App", fixture.ConnectionString);
        builder.UseSetting("Pragmatic:MaintenanceMode:EnableOnStartupFailure", "false");

        builder.ConfigureLogging(l => { l.ClearProviders(); l.AddConsole(); });
    }
}
```

**Which environment, and why it changes.** The factory above runs in `Development` because the
skeleton's `Program.cs` registers its identity only there (`if (app.Environment.IsDevelopment())
app.UseDevelopmentIdentity();`), and a host whose endpoints require authorization with **no**
authentication scheme registered refuses to start outside Development. The moment the host gets a real
scheme (`UseJwtAuthentication`, `UseOidcAuthentication`, …) registered unconditionally, move the factory
to `builder.UseEnvironment("Testing")`: the committed `appsettings.Development.json` (a development
identity, a seeded administrator, a relaxed token) then stops deciding what the tests run against.
Both reference applications are past that point and both use `Testing`.

`Pragmatic:MaintenanceMode:EnableOnStartupFailure = "false"` is there so that a host which fails to
start **fails the test**. Without it, a startup failure (a migration that does not apply, a missing
connection string) puts the host into maintenance mode: it keeps running and answers every request
with `503`, so the test fails later and for the wrong reason, on a status code instead of the
exception that caused it. The key can only switch the behaviour off; `"false"` (any casing) is the one
value it reads.

A test then injects `PostgresFixture` (via `[Collection(IntegrationCollection.Name)]`), creates a
`{{App}}WebFactory`, and exercises the HTTP endpoints with `factory.CreateClient()`. See the
showcase pattern in `examples/showcase/tests/Showcase.IntegrationTests/` for richer scenarios.

`WebApplicationFactory` runs the host inside the test process, so it cannot prove that the data
survives a restart: there is no process to kill. For that (the service killed and started again, the
database container stopped and started), run the built host as its own process, as in
`../cookbook/restart-and-persistence-tests.md`.

## Optional feature packages

The base set above covers CRUD + endpoints + persistence. Opt-in features add packages (and to
`Directory.Packages.props`). Add the boundary package to the relevant library and the host package to
the host.

| Feature | Boundary library | Host |
|---|---|---|
| `events` (in-process domain events) | `Pragmatic.Events` | `Pragmatic.Events.EFCore` |
| `messaging` (bus / outbox / saga) | `Pragmatic.Messaging` | `Pragmatic.Messaging` + `.Channels` or `.RabbitMQ` (+ `.EFCore` for outbox) |
| `jobs` (background / recurring) | `Pragmatic.Jobs` | `Pragmatic.Jobs` (+ `.EFCore` for distributed) |
| `caching` (`[Cacheable]`/`[InvalidatesCache]`) | `Pragmatic.Caching` | none (wired from the module's declaration) |
| `multiTenancy` | n/a | `Pragmatic.MultiTenancy.AspNetCore` (tenant resolution middleware) |
| roles and permissions | `Pragmatic.Authorization` (already there through `Pragmatic.Actions`) | `Pragmatic.Identity.AspNetCore` (already there, unless `[AnonymousHost]`) + a real scheme |

## Build & run

```powershell
dotnet build {{App}}.slnx
dotnet test {{App}}.slnx          # Testcontainers needs Docker running
dotnet run --project src/{{App}}.Host
```
