---
name: pragmatic-choose-modules
description: Use when the user describes a feature to build and needs the Pragmatic.Design NuGet packages and public patterns chosen before implementation, even without the framework's source.
---

# Pragmatic Choose Modules

**Covers:** Choose the right Pragmatic.Design .NET NuGet packages and public patterns for a consumer app feature when source code may be unavailable.

Use this skill to turn a feature request into a concrete Pragmatic.Design stack.

## Workflow

1. Read `../pragmatic-ecosystem/references/packages.md`, `modules.md`, and `patterns-map.md`.
2. Identify which consumer app feature the request maps to (entities/persistence, actions/endpoints, auth, messaging, jobs, caching, logging).
3. Select the minimal package set.
4. Name the follow-up skills to use (see Skill Routing).
5. Call out integration risks: persistence provider, auth model, messaging/outbox, background work, diagnostics, generated code.

## Decision Rules

- Host/bootstrap/DI/modules/startup steps: `Pragmatic.Composition`.
- Entity state, repositories, queries, identifiers: `Pragmatic.Persistence`; add `Pragmatic.Persistence.EFCore` for EF Core runtime.
- Business commands/queries/mutations: `Pragmatic.Actions`.
- HTTP API exposure: `Pragmatic.Endpoints`.
- Input validation: `Pragmatic.Validation`.
- DTO/entity conversion or EF projection: `Pragmatic.Mapping`.
- Business errors: `Pragmatic.Result`.
- Guard clauses for programming errors: `Pragmatic.Ensure`.
- Partial update semantics: `Pragmatic.Patch`.
- Cross-boundary async work: `Pragmatic.Messaging`.
- Scheduled/delayed/background work: `Pragmatic.Jobs`.
- Permission checks/resource authorization: `Pragmatic.Authorization`; identity/current user: `Pragmatic.Identity`.
- Cache keys/invalidation/stampede protection: `Pragmatic.Caching`.
- Dates, clocks, business days, cron helpers: `Pragmatic.Temporal`.
- In-process domain events: `Pragmatic.Events`.
- Schema migrations: `Pragmatic.Migrations` (host).
- Personal data classification, erasure, processing register: `Pragmatic.Privacy`.
- Multi-tenant isolation: `Pragmatic.MultiTenancy` (+ `.AspNetCore` on the host).
- Files: `Pragmatic.Storage` (+ a provider package); images: `Pragmatic.Imaging`; documents: `Pragmatic.Documents.*`.
- Email: `Pragmatic.Email`; multi-channel notifications: `Pragmatic.Notifications`.
- Retry/circuit breaker/timeout on outbound calls: `Pragmatic.Resilience`.
- Runtime toggles and rollouts: `Pragmatic.FeatureFlags` (+ `.Configuration` for flags in appsettings).
- Typed options; settings changed at runtime, per tenant or user, secrets: `Pragmatic.Configuration`
  (+ a backend package, + `.Management` for admin endpoints).
- Comments, tags, attachments, notes on an entity: `Pragmatic.Comments` / `.Tags` / `.Attachments` / `.Notes`.
- A verifiable record of who did what: `Pragmatic.Audit` (+ `.EFCore`); security incidents and their
  reporting deadlines: `Pragmatic.Incidents` (+ `.Audit` for detection from the trail).
- Encryption at rest, per-subject keys and crypto-shredding: `Pragmatic.Cryptography` (+ `.EFCore`).
- Local accounts, sign-in, JWT/OIDC: `Pragmatic.Identity.*`.
- A typed client for another .NET process or TypeScript: `Pragmatic.Client`.
- Several services or instances: `Pragmatic.Agent.Client`, `Pragmatic.Discovery`, a broker for
  `Pragmatic.Messaging`, the gateway built from source.
- Tests: `Pragmatic.Testing` (+ its source generators for contract tests, mocks, comparers).

## Skill Routing

After selecting packages, point to the skill that implements them:

- Project structure, boundary/module split, databases: `pragmatic-architecture`.
- DI, hosting, modules, startup: `pragmatic-use-composition`.
- Entities, repositories, queries, EF Core: `pragmatic-use-persistence`.
- Actions, mutations, HTTP endpoints: `pragmatic-use-actions-endpoints`.
- Result, Ensure, Validation, Mapping, Temporal, Specification, Patch: `pragmatic-use-foundation`.
- Permissions, identity, JWT, data ownership: `pragmatic-use-authorization`.
- In-process domain events, `[Raises<T>]`: `pragmatic-use-events`.
- Async events, outbox, sagas: `pragmatic-use-messaging`.
- Background and recurring jobs: `pragmatic-use-jobs`.
- Declarative caching: `pragmatic-use-caching`.
- Logging and observability: `pragmatic-use-logging`.
- Internationalization / money / culture: `pragmatic-use-i18n`.
- Multi-tenancy: `pragmatic-use-multitenancy`.
- Acting on behalf of another identity: `pragmatic-use-delegation`.
- Personal data and erasure: `pragmatic-use-privacy`.
- Schema and data migrations: `pragmatic-use-migrations`.
- File storage: `pragmatic-use-storage`.
- Image processing: `pragmatic-use-imaging`.
- Document/spreadsheet generation: `pragmatic-use-documents`.
- Email: `pragmatic-use-email`.
- Notifications: `pragmatic-use-notifications`.
- Resilience policies: `pragmatic-use-resilience`.
- Feature flags: `pragmatic-use-feature-flags`.
- Configuration: `pragmatic-use-configuration`.
- Entity traits (comments/tags/attachments/notes): `pragmatic-use-traits`.
- Audit trail and security incidents: `pragmatic-use-audit`.
- Who the caller is — local accounts, JWT, OIDC: `pragmatic-use-identity`.
- Time, time zones, business days: `pragmatic-use-temporal`.
- Typed clients: `pragmatic-use-client`.
- Several services or instances: `pragmatic-use-distributed`.
- Tests: `pragmatic-use-testing`.

## Output Shape

Return:

- Selected NuGet packages.
- Required patterns and attributes.
- Files likely to create/edit in the consumer project.
- Validation command.
- Next skill to invoke (see Skill Routing).

Keep it short unless the user asks for architecture depth.
