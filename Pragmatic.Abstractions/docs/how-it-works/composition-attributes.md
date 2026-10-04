# Composition attributes: declaring what the host must wire

> **Scope**: `src/Pragmatic.Abstractions/Composition/Attributes/`, 16 files, 21 public types (four
> files declare more than one). `[Service]` · `[Decorator]` · `[Inject]` · `[ServiceFactory]` ·
> `[Factory]` · `ServiceLifetime` · `[Module]` · `[IncludeModule<T>]` ·
> `[Include<…>]` · `[PragmaticDatabase]` · `[RequiresConfig]` · `[StartupStep]` ·
> `[RemoteBoundary<T>]` · `[UsePackage<T>]` · `[PragmaticMetadata]`
>
> **Not covered here**: the types these attributes point at (`DatabaseProvider`,
> `PragmaticDatabase`, `MetadataCategory`, `IPackageDefinition`) live in the rest of the
> `Composition/` namespace; see [composition-core](composition-core.md). `[NeedsStep]` is *not* in
> this package: it ships from `Pragmatic.Composition.Host`, alongside the runtime that executes
> startup steps.

For the member-by-member catalogue, see
[interfaces](../interfaces.md#15-composition-attributes). This document is about what each
declaration causes to be generated, and why the shape is what it is.

## The idea

Every one of these attributes is read by the source generator at compile time, and what runs is
ordinary, readable registration code emitted into the assembly: `services.TryAddScoped<...>()`, not
a scan. That is the whole reason the framework can claim no reflection on the request path: a
declaration is an input to code generation, not a marker to be discovered per call. Reflection is
confined to the registration path, which runs once at startup (`ActivatorUtilities.CreateInstance`
for a `[Service]` carrying `[Inject]` members and for every `[Decorator]`), and to the runtime reader
of `[PragmaticMetadata]` described below, which is a fallback.

The consequence for a reader is that every question of the form "where does this get registered" has
an answer you can open: the generated file.

## Services

`[Service]` on a class registers it. Two defaults carry the weight: the lifetime is **Scoped**, and
the service type is the **first implemented interface**. Both are overridable:
`[Service(Lifetime = Lifetime.Singleton)]`, `[Service<IPaymentProvider>]` to name the
interface explicitly, `AsSelf` to register the concrete type, `Key` for a keyed registration.

When both `As` and `AsSelf` are set, **`As` wins and `AsSelf` is ignored**. This is one of the
places where the contract is stated in prose and enforced in a specific branch order in the
transform; the generator carries a comment saying so, because getting the order wrong makes `As`
disappear silently.

`ServiceLifetime` is Pragmatic's own enum, and it mirrors
`Microsoft.Extensions.DependencyInjection.ServiceLifetime` down to the numeric values:
`Singleton = 0`, `Scoped = 1`, `Transient = 2`. The mirroring exists so `Pragmatic.Abstractions`
stays free of a dependency on the DI package. Note that the two names collide: in a file that has
both namespaces in scope, qualify.

### `[Inject]`

`[Inject]` marks a property or a method to be filled from the container after construction. The
class must also be `[Service]`: an `[Inject]` on a type the generator does not otherwise see
produces nothing.

Applying it changes the shape of the emitted registration: instead of a plain `TryAdd`, the
generator emits a **factory lambda** that constructs the instance and then assigns the properties
and calls the methods.

For method injection, parameters are resolved by their declared type, in declaration order. Whether
a parameter is mandatory is decided **per parameter, by its own signature**: an optional or nullable
parameter is resolved with `GetService` and stays null when nothing is registered; a plain one is
required and the container throws when it is missing. Per-parameter keyed resolution is spelled with
`[FromKeyedServices]` on the parameter itself.

`Required` and `Key` on the attribute apply to **property** injection.

`Pragmatic.Abstractions.Analyzers` ships in this package and raises **PRAG1452** on an `[Inject]`
left at `Required = false`, so the permissive default does not survive a build that treats warnings
as errors. ⚠️ It is **PRAG1452**, not PRAG1647: that one is the composition generator's, for
`[Inject]` on an open-generic service, and a `NoWarn` aimed at one would silence the other.

### `[Decorator]`

`[Decorator(Order = n)]` wraps a registered service. Decorators are applied in **ascending** order,
and the lowest order ends up **closest to the original service**: each emitted `.Decorate<>()` call
wraps whatever the previous one produced. The decorated interface is not named on the attribute; it
is the decorator's **first implemented interface**. Implementing none is **PRAG1660**, an error, and
a constructor that does not take a parameter of that interface is **PRAG1661**, also an error.

### `[ServiceFactory]` and `[Factory]`

When construction is not a constructor call, `[ServiceFactory]` marks a class as a factory and
`[Factory]` marks the methods that produce services. The factory class itself is registered as a
**singleton** (it is infrastructure, not state), while each `[Factory]` method registers its
product with its own lifetime, defaulting to `Scoped`.

## Modules

`[Module]` names a boundary library and carries `Version` and `Description`. The attribute is turned
into assembly-level metadata, which is how a host discovers a module it only references. An assembly
has one module (PRAG0628).

A module depends on another with `[IncludeModule<TModule>]`, naming the module's type, so a module
that no longer exists does not compile. It is the only spelling: there is no string-typed
`[Module(DependsOn = ["Billing"])]` and no non-generic form.

Cycles are the generator's problem, not the container's: `ModuleDependencyValidator` builds the
graph and emits **PRAG1602** with the cycle rendered as `A -> B -> A`; a dependency that resolves to
no known module is **PRAG1601**. The check covers modules in the current compilation.

Declaring the dependency does not host it. The host registers exactly its `[Include<T>]`,
`[RemoteBoundary<T>]` and local modules, so a hosted module whose dependency the host neither includes nor
declares remote is **PRAG1603**, reported by the host's generator (`HostedDependencyValidator`).

## Topology: which module writes to which database

`[Include<…>]` is how a host declares its composition, and its three arities are three levels of
explicitness:

| Form | Meaning |
|---|---|
| `[Include<TModule>]` | include the module, no database: no DbContext registration is generated |
| `[Include<TModule, TDatabase>]` | the usual form: the module's entities live in that database |
| `[Include<TModule, TDatabase, TDbContext>]` | pin the DbContext type explicitly |

With the two-arity form the DbContext class name is derived as `{BoundaryName}DbContext`, and the
registration extension as `Add{BoundaryName}DbContext`. The three-arity form exists for when
derivation is not enough (ambiguous boundary names, a duplicate DbContext), and the composition
diagnostics point at it as the way out.

`[PragmaticDatabase]` describes the database itself: a `Provider` and a `ConfigKey`, the
configuration key holding the connection string. It is required for the relational providers, and
leaving it empty on one is **PRAG1609**; `InMemory` and an unspecified provider are exempt, because
an in-memory database is named rather than connected to.

`MigrationConfigKey` is the reason the attribute has two keys instead of one. When set, migrations
use it and everything else uses `ConfigKey`; when unset it falls back to `ConfigKey`. That is the
DDL/DML split: the application connects with a least-privilege account, the migration runner with
one that may alter schema. Every arity of `[Include]` carries it into the generated topology.

That was not always so. The three-arity `[Include<TModule, TDatabase, TContext>]` (the form
`PRAG1607` and `PRAG1652` suggest moving to) read `MigrationConfigKey` and dropped it three lines
later, and both consumers fell back to `ConfigKey` without a word. Taking the suggestion silently
ended the split: migrations kept running, on the least-privilege account.

## Startup

`[StartupStep]` marks a class as a startup step for discovery. It carries no ordering: execution
order comes from the `IStartupStep.Order` property, in the host runtime. The attribute answers "is
this a step", the interface answers "when".

`[RequiresConfig("Section:Path")]` declares that a module cannot run without a configuration
section. The host generator collects every such declaration and emits a `ValidateConfiguration()`
method that the generated entry point calls **before** the application is built: a missing section
stops the boot instead of producing a null halfway through the first request. The method is emitted
only when at least one module declares the attribute; a host whose modules declare none has no
validation method, and nothing to pay for.

## Crossing process boundaries

`[RemoteBoundary<TModule>]` says: this module's code is not here. The generator emits HTTP invokers
for its public actions and **skips** the local invoker and mutation registrations it would otherwise
emit for it. The module's boundary extension is still called, with `BoundaryMode.Remote`, and that
is the call that registers the HTTP implementation. Calling code keeps using the same boundary
interface.

The base URL is resolved from configuration at `Pragmatic:RemoteBoundaries:{ModuleName}:BaseUrl`;
the diagnostics in the `PRAG1685`–`PRAG1688` range cover what goes wrong around it.

`[UsePackage<TPackage>]` imports a prebuilt capability into a module (local identity, for example).
The package's actions are merged into the importing module's generated surface, and its endpoints
get a dedicated route group so they do not collide with the module's own. Each package type is
imported once per module.

## `[PragmaticMetadata]`: the channel, not an input

This one is different in kind: you generally do not write it, the generator does. Around
twenty-five templates emit `[assembly: PragmaticMetadata(MetadataCategory.X, …)]` describing what
they generated: DI registrations, actions, persistence, the API manifest, personal-data
declarations.

The host generator then reads those attributes back **out of referenced assemblies**, at compile
time, through Roslyn. That is what makes cross-assembly auto-discovery work without a runtime scan:
a boundary library ships facts about itself in its metadata, and the host compiles against them.

`Pragmatic.Discovery` can also read the same attribute at runtime by reflection, as a fallback when
the generated registry is empty; that path is annotated `[RequiresUnreferencedCode]`, because it is
the one place where the metadata channel is not trim-safe.

## A name to watch

`Pragmatic.Actions.Mutation` declares its **own** `IncludeAttribute`, non-generic, and meaning
something entirely different: an EF Core eager-loading path, `[Include("Lines.Product")]`. The
namespaces differ, but a file that has both in scope will need to qualify.

## External references

Named here, described where they live:

- **`Pragmatic.Abstractions.Analyzers`**: ships with this package; `InjectRequiredAnalyzer` raises
  PRAG1452.
- **`Pragmatic.SourceGenerator` → `Features/Composition/`**: the transforms and templates that read
  every attribute above, plus `ModuleDependencyValidator` and `CompositionDiagnostics`
  (PRAG1600–1699).
- **`Pragmatic.Composition.Host`**, the runtime side: `IStartupStep` and its ordering,
  `[NeedsStep]`, the decorate extension the generated `.Decorate<>()` calls resolve against.
- **`Pragmatic.Discovery`**: `HostTopologyInfo`, the runtime reader of `[PragmaticMetadata]`.
- **`Pragmatic.Migrations.Cli`**: `ConnectionStringResolver` turns a config key into the connection
  string a migration should run against; the key itself comes from `[PragmaticDatabase]`, read by
  the generator.
- **`Pragmatic.Actions` → `PragmaticModuleMetadataAttribute`**: a second assembly-level metadata
  channel, read by the generator alongside `[PragmaticMetadata]`.
