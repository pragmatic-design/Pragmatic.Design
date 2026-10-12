# The clock, and when the database refuses

Part of `pragmatic-use-persistence`, which keeps what most tasks need. This file holds
what a task reads only when it reaches it.

## The clock a read and a write evaluate against

Every generated repository takes an optional `TimeProvider` and uses it for two things: the audit
columns where the entity has them, and `FilterContext.Now`, the instant a temporal filter evaluates
against.

```csharp
// In a test host, so a recorded instant becomes a value you can name.
services.RemoveAll<TimeProvider>();
services.AddSingleton<TimeProvider>(pinned);
```

⚠️ **`FilterContext.Now` is `required` on purpose.** A default of `UtcNow` would let any read that
forgot it consult the wall clock silently: it could not fail and it could not be pinned. Required, "as
of when" is something a caller states.

⚠️ **Pinning it is what makes a temporal assertion a measurement.** Without it, every test asserting a
recorded time is a range (`BeCloseTo(UtcNow, 1 minute)`), which passes on a stamp taken from anywhere.
With it, the assertion is an equality, and moving the clock by a known amount is how you tell two
writes apart. Note the second part: with a frozen clock two writes moments apart carry the **same**
instant, so a test that distinguished them by "the column moved" needs `Advance` between them.

## When the database refuses

A unique index, a foreign key, a null or length constraint are rules **the application declared**
(`[LogicKey]`, a relation, `[Required]`), and the database is only where they are enforced. Violating
one does not surface as a provider exception:

```
POST /api/workspaces/members   →  409, {"title": "...", "status": 409}
```

The unit of work classifies the provider's exception and throws a
`PersistenceRuleViolationException` carrying an `IError`; the host's exception mapping renders it with
that error's status. An operation that wants to react (an import saying which row was refused)
catches it and reads `.Error`:

```csharp
catch (PersistenceRuleViolationException violation)
{
    rejected.Add(new RejectedMember(index, row.ExternalId, violation.Error.Code));
}
```

The base `Pragmatic.Result.EFCore` carries a provider-agnostic heuristic; adding
`Pragmatic.Result.EFCore.<Provider>` replaces it with the provider's own error codes.

**A delete a relation refuses is a conflict that says what holds the row.** Deleting a row that a
restricting relation still points at (`OnDelete = Restrict`, or a required reference left at its
default) answers `DbInUseError`: **409**, code `ENTITY_IN_USE`, with `entityType` (what was being
deleted) and `usedBy` (what references it) in the problem body. The names come from the model: the
violated constraint's foreign key, or the one relation into that type when the provider does not name the
constraint. Nothing needs checking by hand before the delete: the declared rule answers. A write that
names a *missing* row is still `DB_CONSTRAINT` (400): there the request is wrong.

```
DELETE /api/absence-kinds/{id}  →  409 {"code":"ENTITY_IN_USE","entityType":"AbsenceKind","usedBy":"Allowance"}
```

A Delete mutation exposed with `[Endpoint]` answers **204 and no body** unless it declares
`[ReturnsDto<T>]` (or a key `ReturnType`): the removed row is not an answer, and serialised it is every
public property the entity has.

This holds for a repository write too, not only for a mutation: the generated repository's
`SaveChangesAsync` goes through the boundary's unit of work (the same instance an invoker holds), so
it classifies, hands over the entity's domain events, and records the save the same way. A concurrency
conflict is the one thing not reframed: it stays a `DbUpdateConcurrencyException`, because a stale row
is two writers meeting rather than a rule the schema enforces, and a concurrency-aware entity's
repository answers it with `ConcurrencyError`.

⚠️ **A failed save leaves its entity tracked, and the next save retries it.** That is a change tracker,
not a defect, but it means one refused row would make every later commit of the same request fail for
the old reason. `IUnitOfWork.Detach(entity)` stops tracking it, and the mutation pipeline calls it on
every save path. Reach for it yourself only when you save through the unit of work directly: without
it, `[CommitStrategy(CommitMode.PerStep)]` cannot keep what came before the failure, which is the whole
of what it promises.
