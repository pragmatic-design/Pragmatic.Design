# Roadmap

Pragmatic.Design is in **1.0.0-alpha**: every package is built, tested and usable, and the public
surface can still change before 1.0. Until the packages are on nuget.org they are consumed from a local
feed ([`howto/local-nuget-server.md`](howto/local-nuget-server.md)). Each module's `README.md` states
where that module stands.

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

## Beyond this repository

The framework provides the mechanisms for GDPR, NIS2 and CRA: encryption with crypto-shredding, an
append-only audit trail, privacy classification with a generated record of processing, incident
windows. It does not make an application compliant: legal basis, purpose and retention remain
decisions of whoever operates it.

## Contributing

Open an issue or a discussion before substantial work. [CONTRIBUTING.md](../CONTRIBUTING.md) describes
how a change is accepted. When this document and the code disagree, the code is right.
