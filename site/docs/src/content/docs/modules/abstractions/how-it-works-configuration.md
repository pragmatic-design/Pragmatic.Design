---
title: "Configuration: values and secrets, kept apart"
description: "> **Scope**: `src/Pragmatic.Abstractions/Configuration/`, 10 files."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/configuration.md
sidebar:
  order: 10
---
> **Scope**: `src/Pragmatic.Abstractions/Configuration/`, 10 files.
> `IConfigurationStore` · `ISecretStore` · `IWritableSecretStore` · `SecretEntry` ·
> `SecretReference` · `ConfigurationChange` · `IConfigurationChangeHandler<TOptions>` ·
> `ISensitiveKeyClassifier` · `NullSensitiveKeyClassifier` · `EnvironmentProfile`
>
> **Not covered here**: the `[Configuration]` and `[Sensitive]` attributes live in the
> `Pragmatic.Configuration` module, not in Abstractions; they are what turns a class into bound,
> validated options and what feeds the generated classifier described below. Store implementations
> are named where they matter, not opened.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#10-configuration).

These types sit in namespace `Pragmatic.Configuration`, not `Pragmatic.Abstractions.Configuration`.
That is deliberate across the framework: a consumer references one namespace whether it gets the
contract from the base package or the implementation from the module.

## Two stores, because they are not the same problem

`IConfigurationStore` reads and writes configuration values by hierarchical key and can watch them
for change. `ISecretStore` reads secrets, `IWritableSecretStore` adds writing.

Splitting them is not tidiness. A configuration value can live in a JSON overlay, a database table,
Redis, Consul, a Kubernetes ConfigMap. A secret must live somewhere that controls access and
rotation, and it carries metadata a configuration value has no notion of: expiry, last rotation. In
the repository the two sets of backends overlap only partly: configuration stores ship for in-memory,
database, Azure App Configuration, AWS Parameter Store, Redis, Consul, Kubernetes and the Agent;
secret stores ship for in-memory, HashiCorp Vault, Azure Key Vault, AWS, GCP, Kubernetes and the
database. Each is caching-wrapped by its own decorator in `Pragmatic.Configuration`.

## `secret://`: the one place the two meet

A configuration value may hold a reference instead of a value: `secret://Booking:ApiKey`.
`SecretReference` is the small static parser for that form: `IsReference`, `TryParse`.

`SecretResolvingConfigurationResolver` in `Pragmatic.Configuration` calls it at every read point and
substitutes the value fetched from the `ISecretStore`. The consequence is the point of the design:
where the reference form is used, the secret never enters the configuration store at all. What is
stored, dumped, exported or reviewed in a database table is the reference; the value is fetched from
the secret backend on the way out, in process.

Nothing enforces that. `IConfigurationStore.SetAsync` takes a string and will store whichever one it
is given; `SensitiveWriteGuardConfigurationStore` decorates it to log a warning when a `[Sensitive]`
key is written as plaintext, and then writes it, because a backend may legitimately encrypt at rest.
The guard makes the exposure discoverable, not impossible.

## Sensitive keys, decided at compile time

`ISensitiveKeyClassifier` answers one question (is this configuration key sensitive?) and the
answer is baked in by the generator rather than discovered at runtime.

When at least one property in the application is marked `[Sensitive]`, the generator emits
`{Prefix}ConfigurationSensitiveKeyClassifier` into `_Infra.Configuration.SensitiveKeys.g.cs`: a
literal `HashSet<string>` of `{SectionPath}:{Property}` keys, case-insensitive, with no reflection
anywhere in the emitted code. The generated aggregator registers it with `AddSingleton`, which wins
over the `TryAddSingleton` of `NullSensitiveKeyClassifier` done by
`AddPragmaticConfiguration`. With no `[Sensitive]` property, nothing is generated and the null
classifier stays, classifying nothing.

Matching ignores the overlay prefix: `staging/Booking:ApiKey` is matched as `Booking:ApiKey`,
because sensitivity is a property of the key's shape and not of the environment the value was
written for.

What the answer is used for: `SensitiveWriteGuardConfigurationStore` warns when a key the classifier
calls sensitive is written as a plain value rather than as a `secret://` reference, steering the
value toward the secret store instead of silently accepting it. `Pragmatic.Configuration.Database`
uses the same classifier to mask values in its audit records, and `Pragmatic.Agent.Client` repeats
the write guard on its own store.

## Secret metadata, and who is expected to supply it

`SecretEntry` carries the value plus `ExpiresAt` and `RotatedAt`. `ISecretStore` exposes it through
`GetSecretWithMetadataAsync`, whose default implementation simply wraps `GetSecretAsync` and reports
no expiry and no rotation: a store that cannot know is honest about not knowing rather than
guessing.

A backend that does know overrides it. `AzureKeyVaultSecretStore` populates both fields from the
vault's own properties; any store whose backend exposes TTL or versioning can do the same, and that
override is the entire integration.

The reason it matters is on the reading side: anything that caches a secret must honour `ExpiresAt`.
`CachingSecretStore` computes its entry lifetime as the smaller of the configured TTL and the time
left until expiry, so a cached secret can never outlive the secret itself. `SecretEntry.IsExpired`
treats the expiry instant itself as expired.

## Watching for change

Stores that support watching publish `ConfigurationChange` records: key, old value, new value.
`OldValue` being null means the key did not exist before; it does not distinguish that from a null
that was explicitly stored. Watch semantics are implemented independently by each store, so the
record is the shared shape rather than a shared implementation.

To react in code when a key belonging to a bound options section changes, implement
`IConfigurationChangeHandler<TOptions>` and register it with
`AddConfigurationChangeHandler<TOptions, THandler>()`. That single call is the whole wiring: it
registers the handler and the hosted dispatcher that watches the store and routes changes to it.
There is no attribute and no automatic discovery here: the opt-in is explicit by design, because a
handler that reacts to configuration change is a decision about runtime behaviour, not a
consequence of having declared an options class.

## `EnvironmentProfile`

`EnvironmentProfile` wraps `IHostEnvironment` in the framework's own conventions and is what the
resolution chain in `Pragmatic.Configuration` (the resolver, the caching store, the bridges to
`Microsoft.Extensions.Configuration`) works from when it decides which environment overlay applies
to a read.

## External references

Named here, described where they live:

- **`Pragmatic.Configuration`**: `ConfigurationResolver`,
  `SecretResolvingConfigurationResolver`, `SensitiveWriteGuardConfigurationStore`,
  `CachingConfigurationStore`, `CachingSecretStore`, `ConfigurationChangeDispatcher`: the runtime
  that gives all these contracts behaviour.
- **`Pragmatic.Configuration.Database` / `.Azure` / `.Aws` / `.Gcp` / `.Redis` / `.Consul` /
  `.Kubernetes` / `.Vault`**: the backend packages; each ships a store, a secret store, or both.
- **`Pragmatic.Configuration.Management`**: actions that expose configuration reads and writes as
  a manageable surface.
- **`Pragmatic.Agent.Client`**: `AgentConfigurationStore` and `InProcessConfigurationStore`, the
  agent-hosted path for the same contract.
- **`Pragmatic.Abstractions/FeatureFlags` → `IFeatureFlagStore`**: deliberately a separate
  contract: a flag is evaluated against a context, a configuration key is read.
