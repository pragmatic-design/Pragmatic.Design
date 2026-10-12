# Transactions: what a shape costs, and how a composing action commits

Part of `pragmatic-use-actions-endpoints`, which keeps what most tasks need. This file holds
what a task reads only when it reaches it.

#### How many transactions a shape costs

Measured, not preferred (`CommitCountTests`, `NestedMutationEventsTests`, Showcase, PostgreSQL):

| Shape | `SaveChanges` |
|---|---|
| Three mutations invoked as independent roots | **3**, one each |
| Mutations invoked from inside an action of the **same** boundary | **1**, the action's |
| `[CompositeAction]` with 2 steps | **1** |
| A `DomainAction` that builds N entities with setters | **1** |
| A step in **another** boundary | **its own**: nobody else can save its `DbContext` |

The rule behind the numbers: **the outermost invoker that owns a unit of work commits it, once.**
Anything nested inside it holding the same unit of work stages its writes and defers its events; the
root saves and then flushes them. It is decided by unit of work identity at runtime, so it holds
however the chain is written: through a service, a helper, a loop.

So the choice is about meaning, not cost:

- **A mutation** for one entity written from named inputs: property mapping, L1/L2 validation,
  permissions, events and the endpoint, for free. Invoking one in a loop from an action is a normal
  thing to do and costs one transaction; see `CancelReservationsForPropertyAction`.
- **`[CompositeAction]`** when the steps are known at compile time and must be atomic as a set. The
  steps may be **mutations, actions or void actions**, in any mix; each is a property typed as the step
  type, and the generated invoker runs them in order. **Declare the steps and nothing else**: the base
  demands an `Execute` and a composite has no use for one, so the generator writes it. (It throws:
  being called would mean the composite was invoked around its own invoker.) Write one yourself and it
  is left alone.
  - It commits **once**, by construction, which is what a composite is. `[CommitStrategy(PerStep)]` on
    one contradicts that and is `PRAG0430`.
  - `[Transactional]` **is** honoured on a composite, and buys the one thing a plain composite cannot
    give: a step that reads what an earlier step wrote. Without it the steps merely stage, so a step
    whose logic is a *query* sees the store as it was before the composite began, and reports success
    having done nothing.
  - ⚠️⚠️ **Exposed with `[Endpoint]`, it must declare its own `[RequirePermission]`.** The steps run as
    internal calls and their permissions are deliberately **not** re-checked: the composite is the
    authorization boundary. A composite without one answers **`204` to a caller holding no permissions
    at all** and writes every row, where each step's own endpoint would have answered `403`. Nothing
    **`PRAG0440`** refuses it; `[AllowAnonymous]` is how a composite says it is deliberately public.
    `RequireAuthorizationByDefault` does not cover this: it demands only that the caller be
    authenticated.
  - ⚠️ **A collection is not a set of steps.** Steps are read one property at a time, and a property
    whose type is not itself a mutation or an action (`List<TMutation>`, an array, anything wrapping
    them) is skipped with no diagnostic (`PRAG0427` fires only at *zero* steps). A variable number of
    children arriving in one nested DTO is `[PartOf<TParent>]` with a `CollectionStrategy`, not this.
  - Over HTTP a step property is an ordinary body property, so the generated request body **nests one
    JSON object per step** and the nested step type is registered in the generated
    `JsonSerializerContext`; the path is AOT-safe. ⚠️ It nests the *mutation type*, not the
    `{Mutation}Body` its own endpoint uses, so what that body would have excluded (the implicit `Id` of
    an `Update` step) is on the wire.
- **Setters inside one action** when the rows are one fact and no step deserves its own name,
  validation or event.


#### Composing, and saying how it commits: PRAG0428

An action that invokes other actions or mutations has **three** possible outcomes, and none of them is
implied by the code. `PRAG0428` asks which one, and only of an action that actually composes; one that
invokes nothing is a single transaction whatever the default and is never questioned.

| Declaration | What it means |
|---|---|
| `[Transactional]` | One database transaction, and **each step saves as it goes**, so a later step reads what an earlier one wrote. Costs a round trip per step. |
| `[CommitStrategy(CommitMode.Once)]` | One commit at the end. Atomic, but the steps cannot see each other: a query in step 2 runs against the store as it was before step 1. |
| `[CommitStrategy(CommitMode.PerStep)]` | Independent steps, each committing on its own. A failure keeps what came before. |

⚠️ **A `[Transactional]` operation can run twice.** The generated host turns on `EnableRetryOnFailure()` for
SQL Server, PostgreSQL and MySQL. A retrying strategy refuses a transaction opened outside it, so the invoker runs
the whole invocation inside it. On a transient failure before the commit (a dropped connection, a failover),
**the operation runs again**: filters, loading, the body and the save, from a cleared change tracker.

- A body must have **no effect outside its transaction**. Mail, messages and events go through the outbox,
  which is transactional.
- A direct HTTP call, a file write, or a call into another boundary (which commits on its own) runs again.
- What follows the commit (events, cache invalidation, after-filters) runs once.

No diagnostic checks this: whether a body reaches outside is not something the generator can read off it.
An application can change the retry, or turn it off, with `AddBoundary<T>(cfg => cfg.UseDatabase(…))`,
which is applied after the generated provider call.

**Say it once on the boundary** when the answer never varies:

```csharp
[Boundary]
[CommitStrategy(CommitMode.Once)]
public partial class KnowledgeBoundary;
```

Every composing action of that boundary inherits it, and an action that needs something else still says
so; the nearer declaration wins. A boundary *is* a transaction boundary, so this is a sentence it can
say; `[Transactional]` is not (`PRAG0431`), because a transaction is opened per invocation and as a
boundary-wide default it would buy a round trip on every action, including the ones that write nothing.

⚠️ Both attributes act on the invoker's **unit of work**, and there is one only when the action belongs
to a boundary. The boundary is inferred from the entity behind an `IRepository` field, which an action
that *only* composes does not have. Name it: `[BelongsTo<TBoundary>]`. Without it the attribute parses
and does nothing, which is `PRAG0432`.

⚠️ A commit strategy governs **one** unit of work. A step in another boundary commits through its own
whatever you write, because no invoker can save a `DbContext` it does not hold. Crossing that line is
what `PRAG0424` reports and `[UndoWith<T>]` or a saga answers.

⚠️ `BatchContext` defers the unit of work you name (`new BatchContext(unitOfWork)`) and **performs no
commit itself**: you save. Opening one also stops the invoker from claiming the commit, which is the
point. The parameterless form still covers every unit of work, including boundaries you cannot save.
