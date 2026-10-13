---
name: pragmatic-new-app
description: Use when the user asks to create or scaffold a new app from the Pragmatic.Design packages (nuget.org, or a local BaGetter feed for unreleased builds), with Composition host, source generators, persistence, actions, endpoints, validation, verification.
argument-hint: "<app-name> [features]"
shell: powershell
---

# Pragmatic New App

**Covers:** Create a new external consumer app using Pragmatic.Design NuGet packages from nuget.org (or a local BaGetter feed for unreleased builds), with Composition host, source generators, persistence, actions, endpoints, validation, and verification.

Use this for an external consumer application that uses Pragmatic.Design packages without inspecting Pragmatic source code. Do not use it for creating a new internal `Pragmatic.*` library module.

## Workflow

1. If the requirements are not fully specified, ask before scaffolding: the bounded contexts, who
   calls the API (step 4), whether rows belong to a tenant, and which of storage, documents, email and
   a clock the application needs; each is a package and a host line. `pragmatic-choose-modules` maps
   the answers to modules.
2. Read `../pragmatic-ecosystem/references/scaffold/solution-template.md` and lay down the **full
   best-practice structure** (do NOT create a single minimal project): `.slnx`, `global.json`,
   `Directory.Build.props`, `Directory.Packages.props` (CPM), `NuGet.config`, `.gitignore`,
   `.editorconfig`, a boundary library per bounded context with its `translations/en.json`, the web
   host, and an integration test project with the Testing generator in it. Replace
   `{{App}}`/`{{Boundary}}` placeholders. The translations and the generated tests are part of the
   skeleton, not extras: without the first a refused field answers with a key and no sentence, and
   without the second every endpoint goes untested unless someone writes the test by hand.
3. Read `../pragmatic-ecosystem/references/cookbook/domain-model.md` first (namespaces, entity
   declaration, relations, and what not to hand-write), then
   `../pragmatic-ecosystem/references/cookbook/crud-web-api.md` for the per-feature code
   (entity, mutation, query, action, host topology) and the **Critical host requirements** below.
4. **Ask who calls the API** before writing the host: it decides a package and a line of `Program.cs`
   (requirement 5 below): an application with users gets `Pragmatic.Identity.AspNetCore`, one that
   deliberately has none gets `[AnonymousHost]`. There is no third way that builds. If step 1 already
   asked, do not ask twice.
5. Wire the host: `AppDatabase`, the `HostModule` with one `[Include<TModule, TDatabase>]` per module,
   `Program.cs` with `UseI18N(i18n => i18n.LocalizeProblemDetails())` + `UsePragmaticMigrations` + the
   identity choice, and `Properties/launchSettings.json`.
6. `dotnet build {{App}}.slnx`, fix PRAG diagnostics. **This is the first state that has to work**:
   the skeleton (a module with no entity yet) builds, and `dotnet run` serves `/openapi/v1.json`.
   It needs no database while no module has an entity: nothing is migrated.
7. Fill domain code per bounded context: entity (`{{App}}.{{Boundary}}.Entities` namespace) ->
   validation -> mutation/query/action -> DTO projection. Each rule's message key gets a line in
   `translations/en.json`, its own rules included.
8. With the first endpoint in place, add the template's `ContractAppFixture.cs` and
   `TheContractTestsRanAtAll.cs`: the Testing generator then runs a contract test per endpoint. Write
   the tests the generator cannot (reads, the business rules, the acceptance criteria) through the
   typed client it generates (`Api.{{Boundary}}.…`), never with route strings. Build, run, then
   `dotnet test` (Testcontainers needs Docker). ⚠️ **An acceptance list that asks for a restart** ("it
   must not lose anything") changes which project that is: the built host as its own process
   (`../pragmatic-ecosystem/references/cookbook/restart-and-persistence-tests.md`) carries every item
   and the contract tests (its section 7), and the `WebApplicationFactory` project is not written.

## Default Package Set

For the full, validated end-to-end recipe see `../pragmatic-ecosystem/references/cookbook/crud-web-api.md`.
Split the app into a **boundary library** and a **web host** with different package sets.

**Boundary library** (entities, mutations, queries, actions):

- `Pragmatic.Abstractions`, `Pragmatic.Result`, `Pragmatic.Ensure`, `Pragmatic.Validation`
- `Pragmatic.Mapping` (DTO `[MapFrom]`/`[GenerateProjection]`, required for `[Query<,>]` projections)
- `Pragmatic.Actions`
- `Pragmatic.Endpoints` **and `Pragmatic.Endpoints.AspNetCore`** (the latter is required: `[Endpoint]` generates `Microsoft.AspNetCore.*` code into the boundary assembly)
- `Pragmatic.Persistence` **and `Pragmatic.Persistence.EFCore`** (the latter is required for the generated entity infrastructure, e.g. `PersistenceId`)
- `Pragmatic.SourceGenerator` as analyzer
- `translations/en.json`, as `<AdditionalFiles>`: the words of the rules' messages

**Web host**:

- `Pragmatic.Composition.Host`, `Pragmatic.Endpoints.AspNetCore`, `Pragmatic.Result.AspNetCore`
- `Pragmatic.Persistence.EFCore`, `Pragmatic.Migrations`
- `Pragmatic.Internationalization.AspNetCore` (`UseI18N`, requirement 3)
- `Pragmatic.Identity.AspNetCore`, unless the host declares `[AnonymousHost]` (requirement 5)
- `Pragmatic.Endpoints.OpenApi` (the contract, requirement 6) and `Scalar.AspNetCore` (its reference
  UI, in Development)
- The EF provider matching Pragmatic's EF Core major (`.NET 10 → EF Core 10`), so `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.*`
- `Pragmatic.SourceGenerator` as analyzer

**Integration test project**: a plain `ProjectReference` to the host (the Testing generator reads the
endpoints from it), `Pragmatic.Testing`, `Pragmatic.Testing.SourceGenerator` (`PrivateAssets="all"`),
`Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`.

Nothing else is required. A capability is wired because a module **declares** it (`[Cacheable]`,
`[ResiliencePolicy]`, `[Job]`, `[MessageHandler]`), not because its package is on the host; the Time
off example references neither Caching, MultiTenancy nor Resilience and starts. Add `Messaging`,
`Jobs`, `Patch`, `Temporal` or `MultiTenancy.AspNetCore` when the application uses them.

## Critical host requirements (skip any and the app fails to build or start)

1. **Entities live in the `{Boundary}.Entities` namespace**: the SG derives the host database root
   namespace from it. A feature-folder namespace breaks the generated migration/schema types.
2. **A host `[Module]` topology class with `[Include<TModule, TDatabase>]`** maps modules to databases
   (+ `[NeedsStep<RoutingStep>]`, `[NeedsStep<InternationalizationStep>]`). Without it: *"No databases
   configured"* → DI failure at startup.
3. **A culture has to come from somewhere, or the host refuses to start.** The skeleton's
   `translations/en.json` gives it: the cultures of the files, defaulting to the one they are written
   from (`[TranslationKeys(DefaultCulture = …)]`, `en` unless set). Without a translation file,
   `app.UseI18N(i => { i.DefaultCulture(CultureCode.EnglishUS); i.Support(...); })` gives it, and
   overrides the declared one when the application wants regional cultures. ⚠️ The lookup is exact: a
   file named `en.json` does not answer requests in `en-US`, so a host that sets `EnglishUS` needs
   `en-US.json`. `LocalizeProblemDetails()` is what puts the sentences in an error response
   (`messages`, beside the keys under `errors`); without it the client gets the keys only.
4. **`IRepository<TEntity>` takes one type argument, the entity**: there is no form that also takes
   the key type.
5. **Who calls the API is a decision, and the build asks for it.** `Pragmatic.Actions` brings
   `Pragmatic.Authorization`, every endpoint requires an authenticated caller by default, and a host
   with neither choice stops on **PRAG1695**:
   - users → `Pragmatic.Identity.AspNetCore`, and in `Program.cs`
     `if (app.Environment.IsDevelopment()) app.UseDevelopmentIdentity();` (the `X-User-*` headers are
     the caller). A real scheme (`UseJwtAuthentication`, `UseOidcAuthentication`) is the security
     pass. ⚠️ Until it is there, outside Development the host **refuses to start** ("the endpoints
     require authorization and no authentication method is configured for environment Production"), so
     run it in Development (`launchSettings.json`) or give that environment a scheme;
   - deliberately no users (a LAN tool, a kiosk, behind an authenticating proxy) → `[AnonymousHost]`
     on the host `[Module]`. It also lifts the refusal to start outside Development: an anonymous host
     runs in Production with nothing more.
6. **The contract is served by the generated host, in Development**: with `Pragmatic.Endpoints.OpenApi`
   referenced it publishes `/openapi/v1.json` (the document the generator wrote at compile time) and,
   with `Scalar.AspNetCore` referenced too, `/scalar`. Write no startup step for either. Other
   environments publish the document only after `app.UseApiDocumentation()`; Scalar stays in
   Development. `Properties/launchSettings.json` runs `dotnet run` in Development; without it the
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

The first `dotnet run`, in Development, prints each log twice, the second time as OpenTelemetry's
multi-line `LogRecord.Timestamp:` block, and a block per trace: the console exporter the host adds in
Development. `Telemetry:Enabled=false` turns it off (see "Build & run" in the solution template).

Two checks that the defaults are in, before calling the application done:

- `dotnet test` lists generated contract tests (`{{Boundary}}CrudContractTests`, and
  `{{Boundary}}AuthContractTests` once an operation requires a permission) and
  `TheContractTestsRanAtAll` passes with the uncovered operations pasted in.
- A request a rule refuses answers `422` with both `errors` (the keys) and `messages` (the
  sentences of `translations/en.json`). No `messages` at all means `LocalizeProblemDetails()` is
  missing, or the file's culture is not the one the request is answered in.

For analyzer/package validation, follow the consumer flow in `../pragmatic-ecosystem/references/verification.md`.
