---
title: "Pragmatic.Patch"
description: "Source-generated PATCH DTOs with tri-state semantics for HTTP `PATCH`."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Patch/README.md
sidebar:
  order: 0
  label: Overview
---
Source-generated PATCH DTOs with tri-state semantics for HTTP `PATCH`.

## The Problem

A nullable property can't express the three states a PATCH request needs:

| State | Meaning | JSON |
|-------|---------|------|
| Field **not sent** | Don't touch it | key absent |
| Field **explicitly null** | Clear it | `"name": null` |
| Field has a **value** | Update it | `"name": "Alice"` |

A `string?` collapses "not sent" and "set to null" into the same `null`. So hand-rolled PATCH endpoints
either overwrite unmentioned fields with null (data loss), skip nulls (can't clear a field), or demand
the full object (defeating PATCH).

```csharp
// Without Pragmatic.Patch: ambiguous
public class UpdateGuestRequest
{
    public string? FirstName { get; set; }  // null = "clear it" or "don't touch"?
}
```

## The Solution

`Optional<T>` (a readonly struct with explicit tri-state) plus source-generated patch types. Mark an
empty `partial record` with `[GeneratePatch<TEntity>]`: the generator emits one `Optional<T>` property
per settable entity property, plus the apply logic that updates only the properties that were actually
sent, and tracks `ModifiedProperties` for change-aware validation/persistence.

```csharp
// You write only this: an EMPTY partial record. Don't declare the properties.
[GeneratePatch<Guest>]
public partial record PatchGuestRequest;

// The generator emits the Optional<T> properties FROM the Guest entity, e.g.:
//   Optional<string>  FirstName { get; init; }
//   Optional<string?> Phone     { get; init; }   // can be sent-as-null to clear
// plus ApplyTo(), ModifiedProperties, and a System.Text.Json converter.

// Only sent fields are applied; "not sent" is left untouched.
patch.ApplyTo(guest);
```

Id / `PersistenceId`, `IAuditable` and `ISoftDelete` members, concurrency tokens, and navigation
properties are excluded automatically.

`Pragmatic.Persistence` has a second tri-state form, `[Patch<TEntity>]`: you declare the properties
yourself as plain nullable types, and the generated JSON converter records which ones the body named
(`MarkSet`) so that `ApplyPatch` can tell "sent as null" from "not sent". This package generates the
whole DTO from the entity instead, with `Optional<T>` making the state part of each value; see
[Persistence: Patch](/modules/persistence/13-patch/).

## Installation

```bash
dotnet add package Pragmatic.Patch
dotnet add package Pragmatic.SourceGenerator   # generates the patch types
```

## Status

**Functional** within 1.0.0-alpha: `Optional<T>`, `[GeneratePatch<T>]` generation, JSON deserialization,
and `ModifiedProperties` tracking. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/patch/concepts/) | Tri-state, `Optional<T>`, generated apply logic, change tracking |
| [Getting Started](/modules/patch/getting-started/) | Your first patch DTO and `ApplyTo` |
| [Tri-State Semantics](/modules/patch/tri-state-semantics/) | not-sent vs null vs value, JSON mapping, edge cases |
| [Common Mistakes](/modules/patch/common-mistakes/) | The most frequent patch pitfalls |
| [Troubleshooting](/modules/patch/troubleshooting/) | Problem/solution guide with diagnostics |

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/patch/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Patch is **MIT-licensed**.
