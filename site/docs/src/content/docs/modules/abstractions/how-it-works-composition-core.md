---
title: "Composition: the seam between a module and its host"
description: "> **Scope**: `src/Pragmatic.Abstractions/Composition/`, the direct files plus `Database/`, `Enums/`,"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/composition-core.md
sidebar:
  order: 9
---
> **Scope**: `src/Pragmatic.Abstractions/Composition/`, the direct files plus `Database/`, `Enums/`,
> `Extensions/`, `Metadata/`. 9 files.
> `IPragmaticBuilder` · `IPackageDefinition` · `IEndpointRouteBuilderConfigurator` ·
> `PragmaticDatabase` · `DatabaseProvider` · `ServiceCollectionDecorateExtensions` ·
> `AssemblyMetadataRegistry` · `IAssemblyMetadataProvider` · `AssemblyMetadataEntry` ·
> `MetadataCategory`
>
> **Not covered here**: `Composition/Attributes/` (`[PragmaticDatabase]`, `[PragmaticMetadata]`,
> `[UsePackage]`, `[Include]`) is the declarative half of the same feature and has its own
> document, [composition-attributes](/modules/abstractions/how-it-works-composition-attributes/). Consumers outside Abstractions are
> named where they matter, not opened.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#14-composition). This document
is about how the pieces fit together, and why they are shaped the way they are.

## The three tiers, and which one lives here

A Pragmatic host is configured at three levels, and confusing them is the usual source of "why is
this not registered".

| Tier | Decided by | Where it is written |
|---|---|---|
| Topology | the source generator, at compile time | `[Include<TModule, TDatabase>]`, `[PragmaticDatabase]` |
| Module strategy | the developer, once per host | the `IPragmaticBuilder` callback in `PragmaticApp.RunAsync` |
| Business wiring | the developer, per concern | `IStartupStep` implementations |

Everything in this folder belongs to the first two tiers: the contracts a host and its modules agree
on before any business code runs.

## `IPragmaticBuilder`: no configuration verbs on it, on purpose

`IPragmaticBuilder` carries three members and no configuration verbs: `Services`, `Configuration`
and `Environment`, the three things every `Use…` needs in order to register anything. Every module
contributes its own verb as an extension method in its own package, following
`PragmaticBuilder{Module}Extensions.Use{Module}()`. Eighteen assemblies do this today:
Authorization, Messaging, the five Identity packages, Agent.Client, MultiTenancy.AspNetCore,
Migrations, Storage, Email, Notifications, Jobs, Logging, Temporal, I18n.AspNetCore, and
Composition.Host itself, which contributes two: maintenance mode and database selection.

The payoff is discoverability by reference: `builder.Use…` offers exactly the modules the host
project actually references, because an extension method is only visible when its assembly is on the
compile line. A single fat interface would offer all eighteen and fail to link on seventeen.

The concrete builder is `PragmaticBuilder` in `Pragmatic.Composition.Host`; the interface is here so
a module package can extend it without depending on the host.

One extension of this kind lives inside Abstractions rather than in its own module:
`Serialization/PragmaticJsonBuilderExtensions.cs`, because the JSON seam is part of the base
package.

## `IPackageDefinition`: a building block that carries its own identity

A building-block package (Identity.Local, Messaging, Configuration.Management,
Authorization.Management) declares itself by implementing `IPackageDefinition` with `static abstract`
members. A module imports it with `[UsePackage<TPackage>]`, which uses the interface as its generic
constraint, so the compiler, not a convention, decides what is importable.

The generator reads `PackageName`, `RoutePrefix` and `Description` into its module model. The route
prefix becomes an entry in the host's package-route map, and the package's actions are merged into
the importing module's action surface. That is why importing a package gives you its endpoints
without writing a single route.

## Databases: the class is the name, the attribute is the configuration

`PragmaticDatabase` is the base type a host derives from to give a physical database a name, and
`[Include<TModule, TDatabase>]` constrains its second parameter to it. In practice these classes are
empty:

```csharp
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class ShowcaseAppDatabase : PragmaticDatabase;
```

Everything the generator needs travels on the attribute. `Provider` selects the EF Core provider
call in the generated host, `ConfigKey` names the configuration entry the connection string is read
from, and the migration path falls back to `ConfigKey` when no separate migration key is given. The
class exists so that `[Include<,>]` has a type to point at: one database, one name, checked by the
compiler everywhere it is referenced.

`DatabaseProvider` is that provider choice as an enum. It is read by the module transform, by the
topology validator, and by the host and entry templates, which turn it into the concrete EF Core
registration.

> Three unrelated types in this repository are named around "database provider":
> `Pragmatic.Abstractions`' `DatabaseProvider` (this one, EF Core provider selection),
> `Pragmatic.Configuration.Database`'s `DatabaseProvider` (SQL dialect of the configuration store),
> and `Pragmatic.Persistence.EFCore`'s `IDatabaseProvider`. They have no type relationship.

## Assembly metadata: two channels, one vocabulary

`MetadataCategory` is the shared vocabulary (24 explicit values, `DI = 0` through
`PersonalData = 23`), naming what a piece of generated metadata is about. Around twenty-five
generator templates emit `[assembly: PragmaticMetadata(...)]` entries tagged with one of them.

There are two ways that metadata is read, and they run at different times.

**Compile time.** The host generator reads the `[assembly: PragmaticMetadata]` attributes of the
referenced assemblies directly from their metadata, through `MetadataReader`. This is how a host
discovers what its modules contain while it is being generated, with no runtime, no reflection, nothing
loaded.

**Runtime.** `AssemblyMetadataRegistry` is a static registry of `IAssemblyMetadataProvider`
instances, each returning `AssemblyMetadataEntry` values: the same `Category`, `SchemaVersion` and
`JsonData` triple as the attribute, in runtime form. The host topology generator emits
`PragmaticHostTopologyMetadataProvider` alongside a `[ModuleInitializer]` that registers it, so the
registry is populated before any user code runs. `Pragmatic.Discovery`'s `HostTopologyInfo` reads it
with `FindByCategory(MetadataCategory.HostTopology)`.

The runtime channel exists for exactly one reason: reading assembly attributes back requires
reflection, which is neither trim- nor AOT-safe. `HostTopologyInfo` tries the registry first and
only falls back to the reflection path, which carries `[RequiresUnreferencedCode]`. A registered
provider is what keeps that fallback unused.

### Why the enum values are written out

The numbers in `MetadataCategory` are a cross-assembly binary contract: the value, not the name,
is what ends up in the compiled attribute of a module and what the host generator compares against.
The generator cannot help enforce this: it targets netstandard2.0 and cannot reference the runtime
enum, so it keeps its own `MetadataCategoryIds` constants. Values are therefore append-only:
renumbering an existing category silently changes the meaning of every already-compiled assembly
that used it.

## `Decorate<>`: decoration without the host package

`ServiceCollectionDecorateExtensions` adds `Decorate<TService, TDecorator>()` and a factory overload
to `IServiceCollection`. It lives in Abstractions so a domain module can decorate a service without
referencing ASP.NET Core or `Pragmatic.Composition.Host`; there is one implementation, and the host
package points at this one.

It is the target of generated code: `[Decorator]` registrations emitted by `ServiceRegistrationTemplate`
and the host startup template resolve to these methods. It is also called by hand where a package
wraps a service it does not own: `Pragmatic.Caching.Redis` decorates `ICacheStack` through the
factory overload.

Two behaviours worth knowing before using it:

- **Keyed registrations stay keyed.** A keyed descriptor is replaced by a keyed decorator, in all
  overloads, so `[FromKeyedServices]` and `GetRequiredKeyedService` keep resolving. Replacing a keyed
  registration with a non-keyed one would break those silently.
- **The inner instance is not tracked by the container.** The decorator is built by a factory that
  constructs the inner service itself, so the container disposes only the outer object. If the
  decorated implementation is `IDisposable`, its disposal is yours to arrange.

Decorators are instantiated with `ActivatorUtilities`, a deliberate, contained use of reflection in
the registration path, which runs once at startup and never on a request.

## Adding routes the endpoint generator does not produce

The generated entry point calls `PragmaticHost.MapAllEndpoints(app)` and then resolves every
`IEndpointRouteBuilderConfigurator` from DI and calls `Configure(app)` on it. That is the seam for
routes no attribute describes (a SignalR hub, a gRPC service, a hand-written endpoint group):
implement the interface, register it, and it is mapped after the generated routes.

`Configure` takes an `object` because Abstractions does not reference ASP.NET Core. What it receives
is the `WebApplication`; cast it to `IEndpointRouteBuilder` in the implementation.

## External references

Named here, described where they live:

- **`Pragmatic.Composition.Host` → `PragmaticBuilder`, `PragmaticApp`**: the concrete builder and
  the host entry point that hands it to your callback.
- **`Pragmatic.Discovery` → `HostTopologyInfo`**: reads host topology from the metadata registry,
  with a reflection fallback.
- **`Pragmatic.SourceGenerator` → `MetadataReader`**: the compile-time reader of
  `[assembly: PragmaticMetadata]` on referenced assemblies.
- **`Pragmatic.Caching` → `ICacheStack`**: decorated through `Decorate<>` by the Redis package to
  add counter support.
- **`Pragmatic.Persistence.EFCore`**: provides the EF Core providers that `DatabaseProvider`
  selects between.
