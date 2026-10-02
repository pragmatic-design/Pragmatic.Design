# Conformance

A Pragmatic application whose only purpose is to **prove what the framework does**.

It has no domain. The entities model nothing: each is chosen to cover a combination of relation,
depth and shape — `Depth1`, `Depth2`, a self-reference, a single navigation, a list of scalars — so
that every combination has a case exercising it end to end.

## How it differs from Showcase

| | Showcase | Conformance |
|---|---|---|
| purpose | how the framework is used in a real domain | what the framework does, shape by shape |
| entities | hotels, reservations, invoices | shapes: `Depth1`, `Depth2`, `SelfRef`, … |
| combinations | the ones the domain meets | the ones a user can declare, including those no domain uses |
| a failing test means | a defect in the framework or in the example | the framework does not do what the case states |

Showcase remains the reference for usage. This is the test bench.

⚠️ A case here documents **what the framework does today**. Where that differs from what a reader
might expect, the case's comment says so rather than presenting the current shape as the intended one.

## The projects

| Project | What it holds |
|---|---|
| `Conformance.Poco` | Mapping alone: no EF Core, no container. The project does not reference EF Core, so the dependency graph enforces what would otherwise be discipline. |
| `Conformance.Catalog`, `Conformance.Sales`, `Conformance.Split` | Entities, mutations, queries and endpoints, one per shape; `Split` puts a module in a second host. |
| `Conformance.Host` | The host that composes them. |
| `tests/Conformance.Poco.Tests` | The mapping cases, in milliseconds. |
| `tests/Conformance.Tests` | The end-to-end cases, over HTTP against PostgreSQL. |

## How a case reads

Each case is a pair:

1. **the entities and DTOs** in `src/`, with a comment saying what they demonstrate;
2. **the test** in `tests/…/Cases/`, which exercises them the way a client would.

The cases cover, among others: one-to-many writes at depths one to four; a single navigation; linked
rows; counted children; a child that validates or authorizes on its own; the created response and its
`Location`; the success status code of each operation; the published contract; a boundary as a wall and
a boundary interface that keeps the permission; queries named by a rule, a list or two keys; a patch
that carries children; and a database restart in the middle of a run.

## Run it

```bash
node scripts/check.mjs --tier docker --only Conformance.Tests      # from the repository root
dotnet test examples/conformance/tests/Conformance.Poco.Tests       # no container needed
```
