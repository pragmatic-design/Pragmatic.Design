---
title: Installation
description: Install Pragmatic Design into a .NET 10 project and get the source generator running.
---

## Prerequisites

- **.NET 10 SDK** or later (C# 14 support required)
- An IDE with Roslyn / source-generator support: Rider, Visual Studio 2022+, or VS Code with the C# Dev Kit

## One-minute quick start

### 1. Create a project

```bash
dotnet new webapi -n MyService
cd MyService
```

### 2. Add the Pragmatic NuGets you need

The packages are on [nuget.org](https://www.nuget.org/profiles/Pragmatic.Design) as prereleases, so every `dotnet add package` below takes `--prerelease`. To try changes that are not released yet, a clone of the repository can pack itself into a [local feed](/guides/local-nuget-server/).

The simplest starting point is a Web API with actions, endpoints, and EF Core persistence:

```bash
# Composition host (required: ties everything together)
dotnet add package Pragmatic.Composition.Host --prerelease

# Domain actions + HTTP endpoints
dotnet add package Pragmatic.Actions --prerelease
dotnet add package Pragmatic.Endpoints --prerelease

# Persistence (pick EFCore for relational)
dotnet add package Pragmatic.Persistence.EFCore --prerelease

# Validation
dotnet add package Pragmatic.Validation --prerelease

# The unified source generator (runs at build time)
dotnet add package Pragmatic.SourceGenerator --prerelease
```

Each NuGet carries its own analyzer/generator references. `Pragmatic.SourceGenerator` is the one unified generator: you add it once and it activates the features it detects in the compilation (see [Feature Detection](/source-generator/feature-detection/)).

### 3. Boot the host

`Program.cs`:

```csharp
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    // All Use*() calls are optional: each module ships a working default.
    // Add strategy calls as you need specific behaviour.
});
```

That's it. `dotnet run` starts the host; endpoints discovered at compile time are already mapped.

### 4. Write your first domain action

```csharp
using Pragmatic.Result;

[DomainAction]
[Endpoint(HttpVerb.Post, "/greet")]
[Validate]
public partial class Greet : DomainAction<string>
{
    [Required, MinLength(1)]
    public required string Name { get; init; }

    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult<Result<string, IError>>($"Hello, {Name}!");
}
```

Build and send a request:

```bash
curl -X POST http://localhost:5000/greet -H 'Content-Type: application/json' -d '{"Name":"Alice"}'
# "Hello, Alice!"
```

No manual endpoint registration, no DI ceremony, no validator wiring.

## Implicit usings

With `<ImplicitUsings>enable</ImplicitUsings>`, the default of the `dotnet new` templates, each Pragmatic package the project references adds the namespaces you write against with it: `Pragmatic.Actions` adds `Pragmatic.Actions.Abstractions`, `.Attributes` and `.Mutation`, `Pragmatic.Endpoints` its attributes and base types, `Pragmatic.Validation` its attributes. That is why the action above imports only `Pragmatic.Result`: `Result` and `IError` come from a package the quick start references only through the others.

- The imports come from the packages the project references **directly**. Reference `Pragmatic.Result` or `Pragmatic.Abstractions` in the project to get theirs.
- `<PragmaticImplicitUsings>false</PragmaticImplicitUsings>` switches off the imports of every Pragmatic package.
- `Pragmatic.Composition.Attributes` (`[Service]`, `[Module]`, `[Include]`) is not imported: its `ServiceLifetime` has the name of the one in `Microsoft.Extensions.DependencyInjection`, which the Web SDK imports, and with both imported every `ServiceLifetime` is ambiguous.

## Package families

Pragmatic ships 45 modules as more than 140 packages; a module is usually a core package plus its integrations. The families you likely need:

| Family | Packages | When |
|--------|----------|------|
| Foundation | `Pragmatic.Result`, `Pragmatic.Ensure`, `Pragmatic.Abstractions` | Always (transitive via most modules) |
| Core | `Pragmatic.Actions`, `Pragmatic.Endpoints`, `Pragmatic.Composition.Host`, `Pragmatic.Events` | Building a service with a domain |
| Persistence | `Pragmatic.Persistence.EFCore`, `Pragmatic.Migrations` | Relational database |
| Messaging | `Pragmatic.Messaging`, `Pragmatic.Messaging.EFCore` (outbox), `.Channels`, `.RabbitMQ` | Async messaging |
| Identity & Auth | `Pragmatic.Identity`, `Pragmatic.Identity.Local`, `Pragmatic.Authorization` | User identity and permissions |
| Observability | `Pragmatic.Logging`, `Pragmatic.Resilience` | Production hardening |
| Documents | `Pragmatic.Documents.Pdf`, `.Docx`, `.Xlsx`, `.Csv` | Generating documents |
| Medium Blocks | `Pragmatic.Comments`, `Pragmatic.Tags`, `Pragmatic.Attachments` | Entity traits (opt-in) |

See [the module catalogue in the sidebar](/) for the full list, each with its own Overview page, Concepts, Getting Started, and API reference.

## Central package management (recommended)

For multi-project solutions, use `Directory.Packages.props` to pin versions once. Pin one exact
version for every `Pragmatic.*` package. Mixing versions is unsupported, and a floating range is
not something to rely on during the alpha (see [Versioning](/reference/versioning/)):

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- One version for every Pragmatic package: the exact one you installed. -->
    <PragmaticVersion>1.0.0-alpha.1</PragmaticVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Pragmatic.Composition.Host" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Actions" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Endpoints" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Persistence.EFCore" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.Validation" Version="$(PragmaticVersion)" />
    <PackageVersion Include="Pragmatic.SourceGenerator" Version="$(PragmaticVersion)" />
  </ItemGroup>
</Project>
```

Then each `.csproj` references without versions:

```xml
<ItemGroup>
  <PackageReference Include="Pragmatic.Composition.Host" />
  <PackageReference Include="Pragmatic.Actions" />
  ...
  <PackageReference Include="Pragmatic.SourceGenerator" PrivateAssets="all" />
</ItemGroup>
```

Keep `PrivateAssets="all"` on the generator when you write the reference by hand: `dotnet add package` adds it for you, because the package is a development dependency. Without it the generator flows on to every project that references this one, and a test project referencing the host generates the host's registrations a second time (`CS0121`, an ambiguous `Add…DbContext`).

## Inspecting the generated code

To see what the generator produces:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

After `dotnet build`, you'll find:

```
obj/Debug/net10.0/generated/
└── Pragmatic.SourceGenerator/
    └── Pragmatic.SourceGenerator.PragmaticSourceGenerator/
        ├── Greet.*.g.cs          ← one file per artifact of the type: invoker, endpoint, validator, …
        ├── _Infra.*.g.cs         ← one registration per feature for the assembly
        └── …
```

Read them. They are standard C#, formatted and commented. ⚠️ `obj/` keeps files from earlier compilations: after renaming or deleting a type, rebuild clean before reading what is there.

## Troubleshooting

**Nothing is generated after `dotnet build`.** You likely didn't reference `Pragmatic.SourceGenerator`. It is the analyzer package: without it, no generation runs. Verify with `dotnet list package | grep SourceGenerator`.

**IDE doesn't see generated types.** The IDE caches analyzer output. In Rider: `File → Invalidate Caches / Clear Cache`. In VS: restart. A full `dotnet build` almost always fixes it.

**`PRAG####` errors at build time.** The generator detected a misuse. See the [Diagnostics reference](/reference/diagnostics/) for the meaning and fix.

**"Module A is active but I didn't add it."** Pragmatic meta-packages bring transitive dependencies. Check `dotnet list package --include-transitive`. Pragmatic modules only activate if a **marker type** is reachable: if you see generation you didn't ask for, a reference is pulling it in.

## Next steps

- [Architecture](/getting-started/architecture/): how the 3-tier model fits together
- [Build with an agent](/getting-started/with-an-agent/): the same path with Claude Code, Codex or another agent
- Pick a module from the sidebar and read its **Overview** / **Concepts**
- [Showcase](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/examples/showcase): full reference app composing 30+ modules
- [The step-by-step recipe](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/marketplace/plugins/pragmatic-design/skills/pragmatic-ecosystem/references/cookbook/crud-web-api.md): from an empty folder to a CRUD API on PostgreSQL, built and run against the packages
- [Samples](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/): every module has a runnable `samples/` project
