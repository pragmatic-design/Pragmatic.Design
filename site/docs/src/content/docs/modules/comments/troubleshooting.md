---
title: "Troubleshooting"
description: "Checklist:"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Comments/docs/troubleshooting.md
sidebar:
  order: 4
---
## Comments Not Generated

Checklist:
- [ ] Entity has `[Entity]` attribute
- [ ] Entity class is `partial`
- [ ] `Pragmatic.Comments` package is referenced
- [ ] `Pragmatic.SourceGenerator` is referenced as analyzer
- [ ] No compilation errors before the SG runs

## Endpoints Not Generated

Checklist:
- [ ] Entity has `[Resource("segment")]` (required for route resolution)
- [ ] Check for PRAG2601 warning in build output
- [ ] `Pragmatic.Endpoints` and `Pragmatic.Endpoints.AspNetCore` are referenced

## Table Not Created in Database

Checklist:
- [ ] Host project references the module with `[HasComments]`
- [ ] Verify `DbContext.{Boundary}.g.cs` includes `DbSet<{Entity}Comment>`
- [ ] Check `_Infra.Persistence.SchemaMetadata.*.g.cs` includes the comment table
- [ ] Run migrations after adding `[HasComments]`

## 500 on POST: Column Does Not Exist

If you see `42703: column "X" does not exist`:
- The migration may have been created before `[HasComments]` was added
- Re-run migrations to pick up the new table schema
- Check that `PersistenceId` column exists (the PK is named `PersistenceId` in the database, mapped from `Id` property)

## 400 on POST: Bad Request

- Verify the request body matches the generated `{Action}Body` DTO
- Check required fields: `Content` is always required for Add
- For Update: send raw string body, not JSON object (single `[FromBody] string`)

## 403 on Update: Forbidden

- **Without ICommentPolicy**: only the author (`AuthorId == currentUser.Id`) can edit
- **With ICommentPolicy**: `CanEditAsync` returned false
- **Edit window expired**: if `EditWindowMinutes > 0` and time exceeded

## Diagnostics Reference

| ID | Severity | Description |
|----|----------|-------------|
| PRAG2600 | Error | `[HasComments]` requires `[Entity]` on the class |
| PRAG2601 | Warning | `[HasComments]` without `[Resource]`: endpoints not generated |

## FAQ

**Q: Can I add comments to multiple entities?**
A: Yes. Each `[HasComments]` entity gets its own independent comment table and actions.

**Q: Can I customize the generated entity?**
A: The entity is `sealed partial`: you cannot add properties. Use the `Metadata` JSON field for custom data.

**Q: How do I query comments with custom filters?**
A: Use the generated `{Entity}CommentDto.Projection` in custom LINQ queries against the DbContext.

**Q: Can I disable threading (replies)?**
A: Yes: `[HasComments(AllowReplies = false)]`. The `ReplyToId` property and self-FK are not generated.

**Q: How do I enable moderation?**
A: `[HasComments(RequireApproval = true)]`. New comments start as `PendingApproval`. A `Moderate{Entity}CommentAction` is generated.

**Q: What happens when I delete the parent entity?**
A: Comments are cascade-deleted by the FK relationship (`DeleteBehavior.Cascade`).
