---
title: "Pragmatic.Tags"
description: "Add normalized many-to-many tags to any Pragmatic entity with a single attribute."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Tags/README.md
sidebar:
  order: 0
  label: Overview
---
Add normalized many-to-many tags to any Pragmatic entity with a single attribute.

`Pragmatic.Tags` is a trait package that generates the shared tag entity, junction entity, actions, and endpoints in the consuming boundary.

> Status: functional within 1.0.0-alpha. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

## The Problem

Tagging looks simple until you try to keep it consistent:

- normalize values
- avoid duplicates
- decide whether tags are curated or free-form
- model the many-to-many relationship
- expose add/remove/list endpoints
- keep usage counts and scope rules coherent

Without a shared implementation, every project solves these details differently.

## The Solution

One attribute activates the tagging pipeline:

```csharp
using Pragmatic.Tags;
using Pragmatic.Persistence.Entity;

[Entity]
[Resource("articles")]
[HasTags]
public partial class Article : IEntity
{
    public string Title { get; set; } = "";
}
```

The generator creates:

- a shared concrete `{Boundary}Tag` entity derived from `TagBase`
- a `{Parent}TagLink` junction entity derived from `EntityTagBase<TEntityId>`
- `Add{Parent}TagAction` and `Remove{Parent}TagAction`
- a read side: `{Parent}TagDto` and the paged `List{Parent}TagsQuery`
- `POST`, `GET` and `DELETE` endpoints under the parent resource
- a `Tags` navigation on the parent entity

## Installation

```xml
<PackageReference Include="Pragmatic.Tags" />
```

Typical consumers also reference:

- `Pragmatic.Persistence`
- `Pragmatic.Endpoints` if HTTP endpoints are desired

## Quick Start

```csharp
[Entity]
[Resource("articles")]
[HasTags(AllowCustom = true, Scope = "content", MaxPerEntity = 25)]
public partial class Article : IEntity
{
    public string Title { get; set; } = "";
}
```

Generated workflows, under `/api/{boundary}/articles/{articleId}/tags` (`{boundary}` is the lowercased
boundary name, or `v1` when the entity has no `[BelongsTo<TBoundary>]`):

- `POST   …/tags` — add a tag to an entity
- `GET    …/tags` — list the tags on an entity (paged)
- `DELETE …/tags/{tagId}` — remove a tag from an entity

Each route is gated on a generated permission constant — `ArticleTagPermissions.Add`, `.Remove`, `.Read`
= `{boundary}.article.tags.{add|remove|read}`, boundary and entity in kebab-case — so a caller without
it receives 403. Without a boundary the first segment is **omitted** (`article.tags.add`), unlike the
other traits, which use `app`: tags predate that fallback and existing grants depend on the shape.

## Attribute Options

| Option | Default | Description |
|--------|---------|-------------|
| `MaxPerEntity` | `50` | Maximum number of tags on a single entity instance. `0` means unlimited. |
| `AllowCustom` | `true` | Whether new tags can be created on the fly. When `false`, the generated add action enforces a **curated taxonomy**: a value that does not already exist in the tag's scope is rejected with a `NotFoundError` instead of being minted. |
| `CaseSensitive` | `false` | Whether `Urgent` and `urgent` are distinct tags. |
| `Scope` | parent entity type name | Namespace for tag isolation or sharing. |
| `SubBoundary` | `{Parent}Tags` | Override generated sub-boundary name. |

## Tag Models

`TagBase` provides the shared tag metadata:

- `Value`
- `DisplayValue`
- `Scope`
- `UsageCount`
- `CreatedAt`, `CreatedBy`

`EntityTagBase<TEntityId>` provides the junction record:

- `ParentEntityId`
- `TagId`
- `AddedAt`
- `AddedBy`

## Custom Policy Hook

Register an `ITagPolicy<TEntityId>` implementation to enforce a curated taxonomy and
react to tag lifecycle events. The generated add/remove actions resolve the policy
from the `DbContext`'s service provider and call these hooks automatically when one
is registered (no policy registered → default behavior). All four members have
default implementations, so override only what you need:

| Member | When the generated action calls it |
|--------|-------------------------------------|
| `Task<bool> IsAllowedAsync(string tagValue, CancellationToken)` | On **add**, before anything else — return `false` to reject the value (`ForbiddenError`). Default allows any non-blank value. |
| `string Normalize(string tagValue)` | On **add**, to normalize the value before storage/matching (overrides the built-in trim/lowercase, honoring `CaseSensitive` only in the default path). |
| `Task OnTagAddedAsync(TEntityId entityId, Guid tagId, string tagValue, CancellationToken)` | On **add**, after the junction is created (audit, projections, notifications). |
| `Task OnTagRemovedAsync(TEntityId entityId, Guid tagId, string tagValue, CancellationToken)` | On **remove**, after the junction is removed. |

```csharp
public sealed class ArticleTagPolicy : ITagPolicy<Guid>
{
    private static readonly HashSet<string> Curated =
        new(StringComparer.OrdinalIgnoreCase) { "news", "opinion", "review" };

    public Task<bool> IsAllowedAsync(string tagValue, CancellationToken ct = default)
        => Task.FromResult(Curated.Contains(tagValue));

    public Task OnTagAddedAsync(Guid entityId, Guid tagId, string tagValue, CancellationToken ct = default)
    {
        // e.g. refresh a read model or emit a domain event
        return Task.CompletedTask;
    }
}

// Registration
services.AddScoped<ITagPolicy<Guid>, ArticleTagPolicy>();
```

## Documentation

Local docs:

- [Concepts](/modules/tags/concepts/)
- [Getting Started](/modules/tags/getting-started/)
- [Common Mistakes](/modules/tags/common-mistakes/)
- [Troubleshooting](/modules/tags/troubleshooting/)

Related modules:

- [Pragmatic.Comments](/modules/comments/overview/)

## Requirements

- .NET 10.0+
- `Pragmatic.Persistence.EFCore` and `Pragmatic.Actions` (endpoints also need `Pragmatic.Endpoints`)
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/tags/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Tags is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
