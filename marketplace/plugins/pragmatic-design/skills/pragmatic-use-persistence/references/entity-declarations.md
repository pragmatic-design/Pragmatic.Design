# Declarations on the entity that generate more than a column

Part of `pragmatic-use-persistence`, which keeps what most tasks need. This file holds
what a task reads only when it reaches it.


Five attributes that carry their weight and are easy to miss.

**`[Invariant]`: a rule about the whole aggregate.** A parameterless instance method returning
`bool` and **at least `internal`**. It runs after the operation's body and *before* persist; a `false`
rejects the operation with `InvariantViolationError` (HTTP 422). Which operations, exactly, is below:
it is not every write.

```csharp
[Invariant("An amenity cannot have more than 20 keywords", MessageKey = "error.too_many_keywords")]
internal bool KeywordsWithinLimit() => Keywords.Count <= 20;
```

⚠️ **`private` does not work, and writing a rule nobody calls from outside as `private` is the natural
mistake.** The invoker is a generated class beside the entity, so it can only call what the entity lets
its assembly call. Five conditions have to hold (parameterless, instance, returns `bool`, at least
`internal`, a name no other invariant of the entity uses), and failing any of them is **PRAG0463**, an
error that names which condition failed.

Use it for what must hold about the *whole* entity. Property attributes validate a field in
isolation; this validates the aggregate, and it is the last gate before the write **on that path**.

**`MessageKey` is how the refusal reaches the caller in their language.** It is an error key's base, as
every other error's is, so the translation files carry `error.too_many_keywords.title` (the sentence)
and `.detail` (what it explains); the `Message` above is what a host with no translation for the key
answers. Write it as a string, not as a `TKeys` constant: a base is a nested type in that class,
whose members are those two suffixes.

Without it the refusal reports `error.invariant.violation`, which **every** invariant in the
application shares: translating it means giving up which rule refused, so an application ends up
sending the English sentence to a caller who asked for another language.

**A `[PartOf]` child carries its own**, and the aggregate's invoker checks them after it has merged
it, which is the only write path a child has, so it is the only place they could be checked. Put the
rule about a line on the line.

**Which operations check it.** A `[Mutation]` checks every rule of the aggregate it writes. A
`[DomainAction]` that loaded the aggregate with `[LoadEntity]` checks the rules it **can answer**:
those whose body reads no navigation outside that action's `Include` list.

⚠️ That qualification is a measurement, not caution. An action includes what its own body needs, so a
rule over a navigation it left out would read an **empty collection**: on an invoice, voiding includes
the lines and not the payments, and a rule over the payments would compare a real amount against
nothing and refuse a void that is correct. Checking it would add a 422 to a request that should
succeed.

⚠️ **Still not every write.** A repository `Add`/`Remove` from a job or a seeding step runs no
invariant, and neither does an action that changes the aggregate without loading it through
`[LoadEntity]`. A rule that must hold everywhere is enforced by the entity's own method, which every
caller goes through.

**`[Audited]` on the entity** records who changed what and when, without a line of your code: with
`Pragmatic.Audit.EFCore` referenced, the host registers `AuditDbContext` and `AddAuditTrail()` on the
entity's database, whose migration creates the trail's tables.

**`[GenerateHierarchy]` on a self-referencing entity** generates `GetDescendants` /
`GetAncestors` as recursive CTEs, so a tree query is one call and one round trip instead of a loop.

**`[Published]` on a boundary's query** declares it a read contract another boundary may consume,
and generates that contract. `ContractName` renames it. Without it, a cross-boundary read has no
declared shape.

**`[SearchAcross("Name", "Code", "Email")]` on a query or grid-filter property**: one search box,
several columns, joined by `||`:

```csharp
[SearchAcross(nameof(Property.Name), nameof(Property.City), IgnoreCase = true)]
public string? Search { get; init; }
```

`IgnoreCase = true` lowers both sides; without it PostgreSQL compares as written, so "rossi" does not
find "Rossi". On a property that is not a `string` it is **PRAG0703**.

And on a filter property, **`[ComplexFilter]`** receives a whole filter object as JSON in one query
parameter: the property's type must be a `[FilterDto<TEntity>]`, and the generated `Apply()` delegates
to that DTO's own `ApplyFilter`. It is how a rich client sends a nested AND/OR structure, not an escape
hatch to arbitrary code.
