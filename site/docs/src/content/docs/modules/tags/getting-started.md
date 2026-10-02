---
title: "Getting Started"
description: "Three concrete scenarios showing how `[HasTags]` fits different taxonomy styles."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Tags/docs/getting-started.md
sidebar:
  order: 2
---
Three concrete scenarios showing how `[HasTags]` fits different taxonomy styles.

---

## Install

```bash
dotnet add package Pragmatic.Tags
```

Typical companion packages (already present in most Pragmatic hosts):

```bash
dotnet add package Pragmatic.Persistence.EFCore
dotnet add package Pragmatic.Endpoints
```

---

## Scenario 1 — free-form tagging

Users tag their own articles with whatever labels they want.

```csharp
using Pragmatic.Persistence.Entity;
using Pragmatic.Tags;

[Entity]
[Resource("articles")]
[HasTags(AllowCustom = true, MaxPerEntity = 20)]
public partial class Article
{
    public string Title { get; set; } = "";
    public string AuthorId { get; set; } = "";
}
```

That's it. After the next build, the following endpoints exist:

- `POST   /articles/{id}/tags`       — add a tag (body: a bare JSON string, e.g. `"urgent"`)
- `GET    /articles/{id}/tags`       — list the tags on the article (paged)
- `DELETE /articles/{id}/tags/{tagId}` — remove

Each endpoint is gated on its own permission: `{boundary}.article.tags.add`, `.read` and `.remove`.

Usage:

```bash
curl -X POST /articles/42/tags \
     -H 'Content-Type: application/json' \
     -d '"Urgent"'

curl '/articles/42/tags?page=1&pageSize=20'
# → { "items": [ { "tagId": "...", "articleId": "...", "value": "urgent",
#                  "displayValue": "Urgent", "scope": "content",
#                  "addedAt": "...", "addedBy": "..." } ],
#     "totalCount": 1, "page": 1, "pageSize": 20 }
```

Because `AllowCustom = true`, any new value creates a `Tag` row on the fly. Because `MaxPerEntity = 20`, the 21st *distinct* tag on a single article is refused with a `ForbiddenError` (HTTP 403).

---

## Scenario 2 — curated taxonomy

Moderators maintain a closed set of tags. Users can attach existing tags but cannot create new ones.

```csharp
[Entity]
[Resource("articles")]
[HasTags(AllowCustom = false, Scope = "editorial-content")]
public partial class Article { ... }
```

Seed the allowed tags at startup (or via an admin UI):

```csharp
public sealed class SeedEditorialTagsStartup : IStartupStep
{
    public int Order => 900;
    public async Task OnStartupAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (!await db.Set<Tag>().AnyAsync(ct))
        {
            db.Set<Tag>().AddRange(
                new Tag { Value = "breaking", DisplayValue = "Breaking", Scope = "editorial-content" },
                new Tag { Value = "exclusive", DisplayValue = "Exclusive", Scope = "editorial-content" },
                new Tag { Value = "opinion", DisplayValue = "Opinion", Scope = "editorial-content" });
            await db.SaveChangesAsync(ct);
        }
    }
}
```

Now:

```bash
curl -X POST /articles/42/tags -d '"breaking"'   # 201 Created
curl -X POST /articles/42/tags -d '"nonsense"'   # 404 — not in the curated taxonomy
```

### Curated + policy validation

Combine `AllowCustom = false` with an `ITagPolicy` for richer validation:

```csharp
public sealed class EditorialTagPolicy : ITagPolicy<Guid>
{
    public string Normalize(string tagValue) => tagValue.Trim().ToLowerInvariant();

    // Returning false rejects the value: the add action answers 403.
    public Task<bool> IsAllowedAsync(string tagValue, CancellationToken ct = default)
        => Task.FromResult(Regex.IsMatch(tagValue, "^[a-z]+$"));

    public Task OnTagAddedAsync(Guid entityId, Guid tagId, string tagValue, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnTagRemovedAsync(Guid entityId, Guid tagId, string tagValue, CancellationToken ct = default)
        => Task.CompletedTask;
}

services.AddScoped<ITagPolicy<Guid>, EditorialTagPolicy>();
```

---

## Scenario 3 — shared scope across multiple entities

Multiple entity types share the same tag pool. Adding `"launch"` to an article and to a product reuses the same underlying `Tag` row.

```csharp
[Entity] [HasTags(Scope = "marketing")] public partial class Article { ... }
[Entity] [HasTags(Scope = "marketing")] public partial class Product { ... }
[Entity] [HasTags(Scope = "marketing")] public partial class Campaign { ... }
```

Analytics queries across all three types become trivial:

```sql
-- "What entities are tagged 'launch'?"
SELECT Value, SUM(UsageCount) AS total
FROM Tags
WHERE Scope = 'marketing' AND Value = 'launch'
```

Or per-entity counts:

```sql
SELECT t.Value, COUNT(DISTINCT at.ParentEntityId) AS articles, COUNT(DISTINCT pt.ParentEntityId) AS products
FROM Tags t
LEFT JOIN ArticleTags at ON at.TagId = t.Id
LEFT JOIN ProductTags pt ON pt.TagId = t.Id
WHERE t.Scope = 'marketing'
GROUP BY t.Value
```

---

## Beyond the three scenarios

### Query the tag collection from code

```csharp
var article = await db.Set<Article>()
    .Include(a => a.Tags)            // collection navigation
    .ThenInclude(at => at.Tag)       // the shared Tag entity
    .FirstAsync(a => a.Id == id);

foreach (var at in article.Tags)
    Console.WriteLine(at.Tag.DisplayValue);
```

### Paged list via the generated query

The generated `List{Parent}TagsQuery` runs over the junction and projects `{Parent}TagDto`
(`TagId`, `{Parent}Id`, `Value`, `DisplayValue`, `Scope`, `AddedAt`, `AddedBy`) — this is exactly
what `GET /articles/{id}/tags` serves.

```csharp
var query = new ListArticleTagsQuery { ArticleId = articleId, Page = 1, PageSize = 20 };

var page = await queryExecutor.ExecuteAsync<ArticleTagLink, ArticleTagDto>(
    query, db.Set<ArticleTagLink>(), ct);

foreach (var tag in page.Items)
    Console.WriteLine(tag.DisplayValue);
```

### Case sensitivity

```csharp
[HasTags(CaseSensitive = true)]        // "CSS" and "css" are different tags
[HasTags(CaseSensitive = false)]       // default — they collapse
```

Flip this on for technology tag sets (`C#` vs `c#`, `iOS` vs `ios`) where casing carries meaning.

### Custom sub-boundary name

```csharp
[HasTags(SubBoundary = "ArticleLabels")]
```

By default the generated sub-boundary is `{Parent}Tags`. Override when the naming feels unnatural in your domain.

---

## Next

- [Concepts](/modules/tags/concepts/) — scope, normalisation, usage counter, comparison with Comments
- [Common Mistakes](/modules/tags/common-mistakes/)
- [Troubleshooting](/modules/tags/troubleshooting/)

Related:

- [`Pragmatic.Comments`](/modules/comments/overview/) — similar trait for rich user-authored content
