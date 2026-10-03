---
name: pragmatic-ecosystem
description: Use when working on an app that consumes Pragmatic.Design without its source — the reference hub for NuGet packages from nuget.org (or a local feed for unreleased builds), source generator setup, patterns, diagnostics and verification.
user-invocable: false
---

# Pragmatic Ecosystem

**Covers:** Consumer-facing Pragmatic.Design .NET context for agents that may not see Pragmatic sources: NuGet packages (nuget.org, or a local BaGetter feed for unreleased builds), source generator analyzer setup, patterns, diagnostics, and verification.

Use this as the shared context layer for building applications with Pragmatic.Design packages, especially when the agent cannot inspect Pragmatic source code.

## Read First

- For the blessed cross-module patterns, doctrine, and antipatterns, read [references/patterns-map.md](references/patterns-map.md).
- For a local dockerized NuGet feed (only to consume builds that are not on nuget.org yet), read [references/nuget-feed.md](references/nuget-feed.md).
- For consumer package selection and setup, read [references/packages.md](references/packages.md).
- For source-generator architecture and paths, read [references/source-generator.md](references/source-generator.md).
- For diagnostic ranges, read [references/diagnostics.md](references/diagnostics.md).
- For validation commands and sample tiers, read [references/verification.md](references/verification.md).
- For an end-to-end build recipe, read [references/cookbook/crud-web-api.md](references/cookbook/crud-web-api.md).
- When the data must survive a restart — of the service or of its database — read [references/cookbook/restart-and-persistence-tests.md](references/cookbook/restart-and-persistence-tests.md): the built host as a process, killed and started again, against PostgreSQL in a container.
- **Before writing the first entity**, read [references/cookbook/domain-model.md](references/cookbook/domain-model.md): boundaries, entities, relations, and the members the generator already writes. Ordered by what actually costs build cycles.

## API Surface References

Dense attribute-first references — read the one matching the module in scope:

- Persistence: [references/api-surface/persistence.md](references/api-surface/persistence.md).
- Actions and Endpoints: [references/api-surface/actions-endpoints.md](references/api-surface/actions-endpoints.md).
- Composition: [references/api-surface/composition.md](references/api-surface/composition.md).
- Temporal: [references/api-surface/temporal.md](references/api-surface/temporal.md).

## Rules

- Build consumer projects from package contracts and examples, not monorepo internals.
- Target `net10.0` and enable nullable/implicit usings unless the user's project says otherwise. With implicit usings the Pragmatic packages referenced directly bring their own namespaces: do not write those `using` lines (details in [references/packages.md](references/packages.md)).
- Use NuGet `PackageReference`; avoid `ProjectReference` in external apps.
- Add `Pragmatic.SourceGenerator` as an analyzer package when the selected Pragmatic package needs generated code and does not bring the analyzer transitively. Keep `PrivateAssets="all"` on it (`dotnet add package` writes it), or a test project referencing the host generates the host a second time.
- The packages are on nuget.org as prereleases: `dotnet add package … --prerelease`, no `NuGet.config` needed. Only to consume unreleased builds, map `Pragmatic.*` to a local feed with package source mapping.
- Composition attributes (`[Module]`, `[Include<…>]`, `[PragmaticDatabase]`, `[NeedsStep<…>]`) are in `Pragmatic.Composition.Attributes`.
- Identifiers (`Guid7`, `OpaqueId`, `ShortGuid`, `Slug`) are in the `Pragmatic.Persistence.Identifiers` namespace of the `Pragmatic.Persistence` package.
- If source is unavailable, rely on package README/docs, generated compiler diagnostics, and consumer samples.
- Paths like `examples/showcase/`, `examples/consumer-samples/`, and `templates/` refer to the upstream Pragmatic.Design repository. They are optional pointers — useful only if that repository is at hand. The skills' inline examples and `references/` are self-contained; never assume those upstream paths exist in a consumer project.

## Skill Selection

Consumer app (building a line-of-business project):

- Setting up a local NuGet/BaGetter feed for unreleased builds, or a restore failing with NU####: use `pragmatic-nuget-feed`.
- Creating a consumer app: use `pragmatic-new-app`.
- Structuring a project into boundary libraries, modules, databases: use `pragmatic-architecture`.
- Choosing packages/patterns without source access: use `pragmatic-choose-modules`.
- Working with DI, hosting, modules, startup steps: use `pragmatic-use-composition`.
- Working with entities, repositories, EF Core, queries, identifiers: use `pragmatic-use-persistence`.
- Working with domain operations and HTTP APIs: use `pragmatic-use-actions-endpoints`.
- Working with Result, Ensure, Validation, Mapping, Temporal, Specification, Patch: use `pragmatic-use-foundation`.
- Working with permissions, identity, JWT, data ownership: use `pragmatic-use-authorization`.
- A session acting **on behalf of** another identity (agent, support, job): use `pragmatic-use-delegation`.
- In-process domain events, `[Raises<T>]`, the event outbox: use `pragmatic-use-events`.
- Working with async events, outbox, sagas: use `pragmatic-use-messaging`.
- Working with background and recurring jobs: use `pragmatic-use-jobs`.
- Working with declarative caching and invalidation: use `pragmatic-use-caching`.
- Working with logging and observability: use `pragmatic-use-logging`.
- Internationalization, translations, money/currency, culture: use `pragmatic-use-i18n`.
- Multi-tenant data isolation and tenant resolution: use `pragmatic-use-multitenancy`.
- Personal data, erasure and access requests, the processing register, per-subject encryption
  (crypto-shredding), or a PRAG2900-2913 build error: use `pragmatic-use-privacy`.
- Schema migrations, data migrations, the `pragmatic-migrate` CLI: use `pragmatic-use-migrations`.
- File storage (local disk / Azure / S3): use `pragmatic-use-storage`.
- PDF/DOCX from `.pdxdoc` templates, mail bodies from `.pdxemail` templates, CSV/XLSX exports: use `pragmatic-use-documents`.
- Image resize/convert/thumbnails, EXIF stripping, QR codes: use `pragmatic-use-imaging`.
- Sending email (SMTP, DKIM, S/MIME, test transports): use `pragmatic-use-email`.
- Sending email/SMS/webhook/in-app notifications: use `pragmatic-use-notifications`.
- Resilience (retry, circuit breaker, timeout) on external calls: use `pragmatic-use-resilience`.
- Runtime feature toggles / rollout / A-B: use `pragmatic-use-feature-flags`.
- Strongly-typed / runtime configuration: use `pragmatic-use-configuration`.
- Comments, tags, attachments, notes on an entity: use `pragmatic-use-traits`.
- Several services or instances — remote boundaries, broker between services, Agent, gateway, discovery,
  cache across instances: use `pragmatic-use-distributed`.
- An append-only, verifiable record of who did what — `[Audited]`, security events, the trail, its
  retention, security incidents and their NIS2 deadlines: use `pragmatic-use-audit`.
- Calling a Pragmatic API from another process (Blazor, MAUI, a service) or from TypeScript: use
  `pragmatic-use-client`.
- Time — the clock, time zones at the API edge, business days and holidays, cron, DST, date types:
  use `pragmatic-use-temporal`.
- Who the caller is — dev headers, local accounts and sign-in, JWT, OIDC/Keycloak, password reset:
  use `pragmatic-use-identity` (permissions and roles stay in `pragmatic-use-authorization`).
- Tests — the host under WebApplicationFactory, generated contract tests, the typed client, mocks,
  test clocks and harnesses: use `pragmatic-use-testing`.

For the full scaffold of a new app, use `pragmatic-new-app`.
