# Pragmatic.Tags.Samples

Runnable console samples for the runtime surface of `Pragmatic.Tags`.

> [!NOTE]
> `Pragmatic.Tags` is a **trait package**: the `[HasTags]` source generator emits the
> shared `Tag` entity, the `{Parent}TagLink` junction, the EF Core configs, the
> `Add`/`Remove` actions, the paged read side and the HTTP endpoints **inside the
> consuming project**. A console app has no such generation pipeline, so these samples
> cover the hand-written runtime types only. For the generated side end to end, see
> `examples/showcase` and its tag integration tests.

## Run

```bash
dotnet run --project Pragmatic.Tags/samples/Pragmatic.Tags.Samples
```

## What is demonstrated (existing surface)

| Sample | File | Surface |
|--------|------|---------|
| 1 | `HasTagsOptionsSample.cs` | `HasTagsAttribute` options + defaults (`MaxPerEntity`, `AllowCustom`, `CaseSensitive`, `Scope`, `SubBoundary`); free-form, curated, shared-scope, case-sensitive, limited variants |
| 2 | `TagEntitiesSample.cs` | Concrete `TagBase` and `EntityTagBase<Guid>` entities; `Id` ⇄ `PersistenceId` forwarding; content/scope/usage fields; composite `(ParentEntityId, TagId)` key |
| 3 | `TagPolicySample.cs` | `ITagPolicy<TEntityId>` default-interface-method behavior (`Normalize`, `IsAllowedAsync`) + a custom policy overriding normalization, block-list validation, and `OnTagAdded`/`OnTagRemoved` lifecycle hooks |

## Not covered (depends on the unimplemented `[HasTags]` SG)

- SG-generated `Tag` / `{Parent}Tag` entities and EF configs
- SG-generated `Add{Parent}Tag` / `Remove{Parent}Tag` / `List{Parent}Tags` actions
- SG-generated HTTP endpoints (curl / HTTP client)
- `Tags` navigation include, paged list query, `UsageCount` increment/decrement,
  `MaxPerEntity` enforcement / `LimitReachedError`, curated-taxonomy seeding

These require the source generator and are intentionally out of scope for runnable samples.
