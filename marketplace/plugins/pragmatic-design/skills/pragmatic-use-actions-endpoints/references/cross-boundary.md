# Handing a mutation a row you already hold, and writing across boundaries

Part of `pragmatic-use-actions-endpoints`, which keeps what most tasks need. This file holds
what a task reads only when it reaches it.

### Handing a mutation a row you already hold

The internal interface publishes a second shape for every non-`Create` mutation that names an entity:

```csharp
// You read the row through the repository; the query filters have already had their say.
var candidate = await _candidates.GetByIdAsync(id, ct);

// Hand it over. Without this overload the mutation would load it again: GetByIdAsync is a query,
// not a FindAsync answering from the identity map, so the second read is real.
await _knowledge.Candidates.SettleCandidate(mutation, candidate, ct);
```

⚠️ **On the internal interface only, and that is not a compromise.** The public one is implemented
over HTTP as well, and a tracked entity does not cross a process boundary: publishing it there would
force a remote method whose only outcome is a throw. ⚠️ And never for a `Create`: supplying an entity
to a mutation whose mode is to create one contradicts it.

⚠️ **A grouped boundary gets a twin per group.** `I{Boundary}{Group}InternalActions` extends the
group's public interface, and the root's internal interface re-declares the property with `new` and
the twin's type, so `boundary.Group.Operation(mutation, entity, ct)` and the ordinary
`boundary.Group.Operation(mutation, ct)` are both on one seam. A group with nothing to add gets no
twin, and "nothing to add" is stricter than it sounds: an ordinary update mutation supplies a
preloaded shape by itself, so only a group whose every member creates ends up without one.

⚠️ **The group's public interface and its twin are two different objects, deliberately.** The public
one resolves to the *guarded* implementation, which lets the invoked operation's own permission be
asked; the twin resolves to the unguarded one the root holds. On a group the distinction is drawn by
which instance answers, not by which interface you inject, so injecting the public group interface
from a caller with no principal fails a permission check rather than skipping it, which is the
opposite of what the shorter name suggests.

⚠️ **Public and internal members of a sub-boundary both sit under the group.** If a call does not
resolve, look at the generated `_Boundary.{Boundary}.Definition.g.cs` rather than guessing: it is the
answer, and it is inspectable.

⚠️ **No endpoint does not mean unreachable from another process.** The host generates a
`/_pragmatic/invoke` dispatcher that routes by action type, so an exported operation with no route is
still callable across a `[RemoteBoundary<T>]`.

⚠️ **Writing your own `ITermExtraction`-style service instead is the mistake to avoid.** It duplicates
a contract that already exists, it does not survive the boundary being split out, and (the part that
bites) a hand-written service has no invoker, so it loses the permission check, validation, the unit
of work and the audit entry that the generated path applies. The symptom is a service full of
`entity.SetXxx(…)` doing by hand what a mutation maps by name.

A hand-written interface is still right for **data** another boundary needs to read (`ISupplierDirectory`
returning contacts). The generated ones are for **operations**.

The generated facade carries `[BoundaryActions<TBoundary>]`. You never write it: it is the marker that
makes a cross-boundary call recognisable to the analyzer instead of matching an `I{X}Actions` name the
compiler cannot check.

#### Writing across boundaries: PRAG0424

Reading across boundaries is a join; writing across them is not. Each boundary owns a `DbContext` and
saves it separately, inner first, so a caller that fails **after** the inner call leaves the inner
writes committed. Measured on a real application: five rows survived a failure, pointing at a parent
row that was never written.

`PRAG0424` warns when one invocation commits into more than one store (not on any cross-boundary
call, which alone is atomic) and asks for a decision. Two ways to record it:

```csharp
// Repair: the inner step knows how to undo itself.
[DomainAction]
[UndoWith<RemoveIngestedText>]
public partial class IngestTextAction : DomainAction<IngestResult>;

public sealed class RemoveIngestedText(IRepository<Term> terms) : ICompensates<IngestResult>
{
    public Task<VoidResult<IError>> Undo(IngestResult committed, CancellationToken ct = default) { … }
}

// Accept: the leftovers are harmless, and the reason says why.
[DomainAction]
[AcceptsPartialWrites("The glossary keeps unreferenced candidates; a nightly job prunes them.")]
public partial class WriteStoryAction : DomainAction<WriteStoryResult>;
```

`[UndoWith<T>]` registers the undo **after** the inner commit succeeds; a later failure in the same
request runs the registered undos in reverse. `T` is an ordinary scoped service (the generator
registers it) implementing `ICompensates<TReturn>` (or `ICompensatesVoid`), and `PRAG0425` is an
error when it does not.

⚠️ **Best effort, in-request, and that is the whole guarantee.** A crash between the inner commit and
the undo leaves the work committed: nothing is durable, nothing is retried. That is where a saga
starts, and note Messaging has its own `[CompensateWith<TAction>]` for saga steps, which is the
durable one and a different attribute.

`PRAG0429` says exactly that, at `Info`, on the call: **an undo across a boundary is a saga's
compensating step without a saga's durability.** It does not ask for a change (the decision was made
and recorded, and warning twice would be nagging); it makes sure whoever reads the call knows what they
are looking at without reading the framework.

⚠️ **Behind a `[RemoteBoundary<T>]` the compensator never runs at all.** It executes in the process that
owns the action; the caller holds an HTTP proxy. Only the host can see this (the module compiles the
same whether it is hosted or called), so the host reports it as `PRAG1689`. Publish an event or model
the undo as a saga; there is no attribute that makes an undo cross a process.

A compensator that itself fails is logged at `Error` and returned to the caller as
`COMPENSATION_FAILED`, carrying both errors: the response says the system is inconsistent, not merely
that the operation failed. Each facade method whose action declares an undo is marked
`[CompensableStep]`, and PRAG0424 goes quiet for callers that invoke those steps, per method, so a
caller never has to compensate what it does not call.

The diagnostic reads the **calls**, not the fields: a facade held and never invoked commits nothing.
