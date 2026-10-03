---
name: pragmatic-new-app
description: Use when the user asks to create or scaffold a new app from the Pragmatic.Design packages (nuget.org, or a local BaGetter feed for unreleased builds) — Composition host, source generators, persistence, actions, endpoints, validation, verification.
argument-hint: "<app-name> [features]"
shell: powershell
---

# Pragmatic New App

**Covers:** Create a new external consumer app using Pragmatic.Design NuGet packages from nuget.org (or a local BaGetter feed for unreleased builds), with Composition host, source generators, persistence, actions, endpoints, validation, and verification.

Use this for an external consumer application that uses Pragmatic.Design packages without inspecting Pragmatic source code. Do not use it for creating a new internal `Pragmatic.*` library module.

## Workflow

1. If the requirements are not fully specified, ask before scaffolding: the bounded contexts, who
   calls the API (step 4), whether rows belong to a tenant, and which of storage, documents, email and
   a clock the application needs — each is a package and a host line. `pragmatic-choose-modules` maps
   the answers to modules.
2. Read `../pragmatic-ecosystem/references/scaffold/solution-template.md` and lay down the **full
   best-practice structure** (do NOT create a single minimal project): `.slnx`, `global.json`,
   `Directory.Build.props`, `Directory.Packages.props` (CPM), `NuGet.config`, `.gitignore`,
   `.editorconfig`, a boundary library per bounded context, the web host, and an integration test
   project. Replace `{{App}}`/`{{Boundary}}` placeholders.
3. Read `../pragmatic-ecosystem/references/cookbook/domain-model.md` first — namespaces, entity
   declaration, relations, and what not to hand-write — then
   `../pragmatic-ecosystem/references/cookbook/crud-web-api.md` for the per-feature code
   (entity, mutation, query, action, host topology) and the **Critical host requirements** below.
4. **Ask who calls the API** before writing the host — it decides a package and a line of `Program.cs`
   (requirement 5 below): an application with users gets `Pragmatic.Identity.AspNetCore`, one that
   deliberately has none gets `[AnonymousHost]`. There is no third way that builds. If step 1 already
   asked, do not ask twice.
5. Wire the host: `AppDatabase`, the `HostModule` with one `[Include<TModule, TDatabase>]` per module,
   `Program.cs` with `UseI18N` + `UsePragmaticMigrations` + the identity choice, and
   `Properties/launchSettings.json`.
6. `dotnet build {{App}}.slnx`, fix PRAG diagnostics. **This is the first state that has to work**:
   the skeleton — a module with no entity yet — builds, and `dotnet run` serves `/openapi/v1.json`.
   It needs no database while no module has an entity: nothing is migrated.
7. Fill domain code per bounded context: entity (`{{App}}.{{Boundary}}.Entities` namespace) ->
   validation -> mutation/query/action -> DTO projection. Build, run, then `dotnet test`
   (Testcontainers needs Docker).

## Default Package Set

For the full, validated end-to-end recipe see `../pragmatic-ecosystem/references/cookbook/crud-web-api.md`.
Split the app into a **boundary library** and a **web host** with different package sets.

**Boundary library** (entities, mutations, queries, actions):

- `Pragmatic.Abstractions`, `Pragmatic.Result`, `Pragmatic.Ensure`, `Pragmatic.Validation`
- `Pragmatic.Mapping` (DTO `[MapFrom]`/`[GenerateProjection]` — required for `[Query<,>]` projections)
- `Pragmatic.Actions`
- `Pragmatic.Endpoints` **and `Pragmatic.Endpoints.AspNetCore`** (the latter is required — `[Endpoint]` generates `Microsoft.AspNetCore.*` code into the boundary assembly)
- `Pragmatic.Persistence` **and `Pragmatic.Persistence.EFCore`** (the latter is required for the generated entity infrastructure, e.g. `PersistenceId`)
- `Pragmatic.SourceGenerator` as analyzer

**Web host**:

- `Pragmatic.Composition.Host`, `Pragmatic.Endpoints.AspNetCore`, `Pragmatic.Result.AspNetCore`
- `Pragmatic.Persistence.EFCore`, `Pragmatic.Migrations`
- `Pragmatic.Internationalization.AspNetCore` (`UseI18N`, requirement 3)
- `Pragmatic.Identity.AspNetCore` — unless the host declares `[AnonymousHost]` (requirement 5)
- `Pragmatic.Endpoints.OpenApi` (the contract, requirement 6) and `Scalar.AspNetCore` (its reference
  UI, in Development)
- The EF provider matching Pragmatic's EF Core major — `.NET 10 → EF Core 10`, so `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.*`
- `Pragmatic.SourceGenerator` as analyzer

Nothing else is required. A capability is wired because a module **declares** it — `[Cacheable]`,
`[ResiliencePolicy]`, `[Job]`, `[MessageHandler]` — not because its package is on the host; the Time
off example references neither Caching, MultiTenancy nor Resilience and starts. Add `Messaging`,
`Jobs`, `Patch`, `Temporal` or `MultiTenancy.AspNetCore` when the application uses them.

## Critical host requirements (skip any and the app fails to build or start)

1. **Entities live in the `{Boundary}.Entities` namespace** — the SG derives the host database root
   namespace from it. A feature-folder namespace breaks the generated migration/schema types.
2. **A host `[Module]` topology class with `[Include<TModule, TDatabase>]`** maps modules to databases
   (+ `[NeedsStep<RoutingStep>]`, `[NeedsStep<InternationalizationStep>]`). Without it: *"No databases
   configured"* → DI failure at startup.
3. **A culture has to come from somewhere, or the host refuses to start.** Once the modules have
   `translations/{culture}.json` they give it — the cultures of the files, defaulting to the one they are
   written from (`[TranslationKeys(DefaultCulture = …)]`, `en` unless set). Before that — the skeleton —
   `app.UseI18N(i => { i.DefaultCulture(CultureCode.EnglishUS); i.Support(...); })`, which also overrides
   the declared one when the application wants regional cultures.
4. **`IRepository<TEntity>` takes one type argument, the entity** — there is no form that also takes
   the key type.
5. **Who calls the API is a decision, and the build asks for it.** `Pragmatic.Actions` brings
   `Pragmatic.Authorization`, every endpoint requires an authenticated caller by default, and a host
   with neither choice stops on **PRAG1695**:
   - users → `Pragmatic.Identity.AspNetCore`, and in `Program.cs`
     `if (app.Environment.IsDevelopment()) app.UseDevelopmentIdentity();` (the `X-User-*` headers are
     the caller). A real scheme — `UseJwtAuthentication`, `UseOidcAuthentication` — is the security
     pass. ⚠️ Until it is there, outside Development the host **refuses to start** — "the endpoints
     require authorization and no authentication method is configured for environment Production" — so
     run it in Development (`launchSettings.json`) or give that environment a scheme;
   - deliberately no users (a LAN tool, a kiosk, behind an authenticating proxy) → `[AnonymousHost]`
     on the host `[Module]`.
6. **The contract is served by the generated host, in Development**: with `Pragmatic.Endpoints.OpenApi`
   referenced it publishes `/openapi/v1.json` (the document the generator wrote at compile time) and,
   with `Scalar.AspNetCore` referenced too, `/scalar`. Write no startup step for either. Other
   environments publish the document only after `app.UseApiDocumentation()`; Scalar stays in
   Development. `Properties/launchSettings.json` runs `dotnet run` in Development — without it the
   first run is in Production, where neither the contract nor requirement 5's development identity is on.

## Implementation Rules

- If examples are available, prefer `examples/consumer-samples` for package consumption and `examples/showcase` for domain patterns.
- Use `PackageReference` for consumer apps; use `ProjectReference` only for in-repo development samples that already follow that pattern.
- `Guid7`, `OpaqueId`, `ShortGuid` and `Slug` are in the `Pragmatic.Persistence.Identifiers` namespace
  of `Pragmatic.Persistence`. There is no `Pragmatic.DependencyInjection` or `Pragmatic.Identifiers`
  package: DI is `Pragmatic.Composition`, identifiers are `Pragmatic.Persistence`.
- Keep the first version minimal and buildable.

## Verification

Run the narrowest relevant command first:

```powershell
dotnet build <project-or-slnx>
dotnet run --project src/<App>.Host      # then GET /openapi/v1.json
dotnet test <test-project> --no-restore -v minimal
```

For analyzer/package validation, follow the consumer flow in `../pragmatic-ecosystem/references/verification.md`.
