# Roadmap

Pragmatic.Design is in **1.0.0-alpha**: every package is built, tested and usable, and the public
surface can still change before 1.0. The packages are on nuget.org as prereleases; a local feed
([`howto/local-nuget-server.md`](howto/local-nuget-server.md)) is only for consuming a build of this
repository before it is released. Each module's `README.md` states where that module stands.

## What the status words mean

| Status | Meaning |
|---|---|
| **Stable** | The API is settled for 1.0. Changes before 1.0 are additive. |
| **Functional** | It works and is tested end to end; names and shapes may still change before 1.0. |
| **Preview** | Implemented and tested, with a narrower surface and less use in the reference applications. |

Stable within 1.0.0-alpha: Abstractions, Ensure, Result, Specification, Mapping, Validation, Temporal,
Storage, and the core entity, repository, query and mutation surface of Persistence. The other modules
are functional or preview, as their README says.

## Before 1.0

- **Settle the functional modules.** Names and shapes that are still moving are fixed, and each module's
  status becomes Stable.
- **Documents:** parsing works in memory today; streaming for large files.
- **Agent:** the daemon runs as a regular .NET executable; a Native AOT build.
- **Imaging:** the macOS native binary is built and stamped in CI; tests that run it on macOS.

## Later

Features an application built on the 1.0.0-alpha.3 packages worked around, kept for after the work
above.

- **Identity and access:**
  - Keycloak multi-realm, and an admin client that creates realms and clients and sends required actions.
  - Several audiences and clients in `KeycloakOptions`/`OidcOptions`.
  - Permission per member of a request or a response.
  - Narrowing a caller's permissions.
  - Revocable links with a read log.
  - A rate limit by route value.
  - A sessions API.
  - Row visibility per user, parametric on the caller.
- **Persistence:**
  - A sequence per tenant.
  - Enums with a stable key and a localized label.
  - A more flexible `[TemporalRelation]`, with an exclusion constraint.
  - Lists of value objects, and JSON columns.
  - Record history on EF Core.
  - Freezing a record.
  - Deactivating a lookup row, and refusing references to it.
  - Custom fields and runtime forms.
  - Polymorphic references checked on write.
  - Queries over several entities.
- **Actions and validation:**
  - Plural forms in error messages.
  - A read against the dependency direction.
  - Presets configured at runtime.
- **Time and i18n:**
  - Working-hours arithmetic.
  - A time zone per tenant.
  - Public holidays by country, and a tenant calendar.
  - Translated enum labels.
- **Notifications and integrations:**
  - Attachments.
  - InApp and Push implementations.
  - Inbound webhooks with signature, timestamp and replay checks, and binding a request's raw body.
  - Barcodes drawn by the PDF engine.
  - A file type for an entity's field.
- **Host and tooling:**
  - An application template with a Dockerfile.
  - The manifest exported and served, with relations, state machines, grid fields and lookups.
  - Validation and permissions in the TypeScript client.

## Beyond this repository

The framework provides the mechanisms for GDPR, NIS2 and CRA: encryption with crypto-shredding, an
append-only audit trail, privacy classification with a generated record of processing, incident
windows. It does not make an application compliant: legal basis, purpose and retention remain
decisions of whoever operates it.

## Contributing

Open an issue or a discussion before substantial work. [CONTRIBUTING.md](../CONTRIBUTING.md) describes
how a change is accepted. When this document and the code disagree, the code is right.
