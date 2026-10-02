---
title: Glossary
description: Core vocabulary used across the Pragmatic.Design documentation.
---

Definitions of the terms used throughout these docs. They describe the library as
it works today.

## Module

A `Pragmatic.{Name}` package providing one capability (e.g. `Pragmatic.Validation`,
`Pragmatic.Persistence`). Modules are composed by the host application; the source
generator detects which modules are referenced and activates the matching features.

## Boundary

A logical grouping inside a module's domain, marked with `[Boundary]`. Actions,
entities, and endpoints are assigned to a boundary (via `[BelongsTo<TBoundary>]`),
which the generator uses to compose per-boundary interfaces and registration.

## Medium Block

A cross-cutting package that ships complete and is used as-is — include it,
configure it, use it. Examples: Comments, Tags, Attachments, Notes. A Medium Block
contributes its entity/actions/endpoints to the host through the generator.

## DomainAction

A unit of business behavior marked with `[DomainAction]` and deriving from
`DomainAction<TResult>` (or `VoidDomainAction`, or a variant declaring its errors). The
generator emits an **invoker** that wires dependencies, runs filters (validation,
authorization) and calls `Execute`.

## Mutation / Query

A write and a read that are declarations rather than code: a `[Mutation]` derives from
`Mutation<TEntity>` and says what it creates, updates or deletes; a `[Query<TEntity, TDto>]`
says which rows, filtered and sorted how, projected into what. Neither needs a body for
what can be declared.

## Trait

An attribute on an entity that brings a whole slice with it — `[HasComments]`,
`[HasTags]`, `[HasAttachments]`, `[HasNotes]`: a child entity, its actions and its endpoints,
generated for that entity.

## Invoker

Generated glue around a `DomainAction`: it resolves the action's dependencies from
DI, applies the configured pipeline (e.g. permission checks, entity loading), and
executes the action. Invokers are generated types — never hand-written.

## Source Generator (unified)

The single `PragmaticSourceGenerator` that hosts most feature pipelines (Actions,
Endpoints, Persistence, Validation, …). A few generators are standalone: `Result`,
the ISO code tables of `Internationalization`, the CSV mapper of `Documents`, and the ones
that read something other than your domain — `Client` (the API manifest) and the three of
`Testing` (contract tests, mocks and comparers, run in the test project). See
[Source Generator](/source-generator/how-it-works/).

## Feature detection

How the generator decides which pipelines to run: `FeatureDetector` probes the
referenced assemblies for marker types and produces a `DetectedFeatures` snapshot.
See [Feature Detection](/source-generator/feature-detection/).

## Topology

The compile-time wiring the generator infers from your code (boundaries, action
assignments, entity configuration) — the first of the three configuration tiers.
See [Architecture](/getting-started/architecture/).

## IPragmaticBuilder

The infrastructural configuration surface (auth, storage, …) used in `Program.cs`
to choose module strategies — the second configuration tier. The generator
registers defaults; you override them with `Use*()` methods.

## IStartupStep

A business-wiring hook (services, filters, OpenAPI, HTTP pipeline) — the third
configuration tier. Multiple steps run in `Order`.

## Result / Maybe

`Result<TValue, TError>` and `Maybe<T>` from `Pragmatic.Result`: explicit
success/failure and presence/absence values used instead of exceptions for
expected outcomes. See [Result](/modules/result/overview/).

## Diagnostic (PRAG####)

A compile-time analyzer/generator message with a stable `PRAG`-prefixed id (e.g.
`PRAG0680`). See the [Diagnostics reference](/reference/diagnostics/).
