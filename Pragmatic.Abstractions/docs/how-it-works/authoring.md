# Authoring: saying what is not built yet, in a way a tool can read

> **Scope**: `src/Pragmatic.Abstractions/Authoring/`, 6 files.
> `Behavior` · `PendingBehaviorException` · `PendingContractAttribute` · `RaisesAttribute<TEvent>` ·
> `RuleAttribute` · `UseCaseAttribute`
>
> **Not covered here**: `EntityLifecycle`, the enum `[Raises<T>]` takes for its `On` parameter,
> belongs to `Abstractions/Events/`. The generator features that read these attributes
> (`PendingContractGenerator`, the Lifecycle and Actions features, `ContractTestGenerator`) are
> named below, not opened.

For the member-by-member catalogue, see [interfaces](../interfaces.md#18-authoring). This document
is about what each marker is for, and which of them a tool actually reads.

## Two kinds of marker

The attributes in this folder are inert at runtime: none of them is a service, and none of them
executes in a request. `Behavior` is the exception: `Behavior.Pending()` builds a
`PendingBehaviorException`, and a body still marked pending throws it when it is hit. What differs
is the reader:

- **`Behavior` / `PendingContract` and `[Raises<T>]`** are compiled against. A generator reads them
  and changes what is emitted.
- **`[Rule]` and `[UseCase]`** are annotation-only by design: no framework tool consumes them.
  They exist for manual traceability (pointing a class or method at the business rule or use case
  it implements) and for external tooling that wants to walk an assembly and build its own
  inventory.

## The pending chain

The problem: an application under construction has endpoints that are declared but not implemented.
Leaving them out of the codebase means the shape is not reviewable; leaving them in with a
`NotImplementedException` means the generated contract tests fail, and a suite that is red for
known reasons stops being a signal at all.

`Behavior.Pending()` **returns** the exception rather than throwing it, so the call site reads
`throw Behavior.Pending();`; the compiler still sees a throw statement, so the method needs no
return value and no unreachable code. What it throws is `PendingBehaviorException`, deriving from
`NotImplementedException` so ordinary handlers still treat it correctly, but distinct as a type, so
"what is still pending?" is an exact search rather than a guess among every `NotImplementedException`
in the solution.

From there the chain is entirely compile-time:

| Step | Where | What happens |
|---|---|---|
| Scan | `PendingContractGenerator` (standalone generator in `Pragmatic.SourceGenerator`) | Triggers on types carrying `[Endpoint]` (both the plain and the generic form), and looks for methods whose whole body is `throw Behavior.Pending()`, expression-bodied or a single statement. |
| Emit | `PendingContractsTemplate` → `_Metadata.PendingContracts.g.cs` | One `[assembly: PendingContract("<fully-qualified-name>")]` per pending endpoint, ordinally sorted and deduplicated. No types are generated, only assembly metadata. |
| Read | `ContractTestGenerator` in `Pragmatic.Testing.SourceGenerator` | Runs in the **test** project, collects `PendingContract` from the compilation's own assembly *and every referenced assembly*, and skips generating a contract test for those endpoints. |

Two decisions in that table are worth pulling out.

**Detection is semantic, not textual.** `PendingContractGenerator` resolves the invocation through
the semantic model and checks all three of method name `Pending`, containing type `Behavior`, and
namespace `Pragmatic.Authoring`. A textual match on "ends with Behavior" would make an application's
own `FooBehavior.Pending()` look like a pending contract, and the failure mode is silent, since the
result is a contract test that quietly stops being generated. There is a regression test for exactly
that case.

**The marker is assembly-level metadata, not a type.** The reader lives in a different compilation
(the test project) and needs to see through project references into the boundary assemblies. An
attribute on the assembly survives to the reference and can be read from metadata; a generated type
would have to be found and matched by convention.

**The trigger is `[Endpoint]`.** A `throw Behavior.Pending()` inside a mutation, a domain action or
a job body compiles and throws like any other, but it produces no `PendingContract`, because those
types are not what the generator scans. The chain is about endpoints whose contract tests must be
suppressed.

## `[Raises<TEvent>]`: the same attribute, two jobs

`[Raises<TEvent>(EntityLifecycle on = EntityLifecycle.Created)]` declares that something raises a
domain event. Where you put it decides whether it does work or only records intent.

**On an entity**, the generator wires the raise for you: the Lifecycle feature emits the code that
raises `TEvent` at the declared lifecycle transition, filling the event's constructor from matching
entity members by name. This is the case where `On` matters: it is the transition being hooked.

**On a mutation or an action class**, the generator wires it too, and this is worth being precise
about: **do not raise the event yourself as well, or it goes out twice.** The transforms read the
attribute off the **class**, never off a method.

**On an entity's method** it generates nothing, and the build refuses it (**PRAG2753**): without it the
event would reach no handler while everything compiled. A generator cannot
add a statement to a body you wrote. The target stays `AttributeTargets.Method` because on a type that
is *not* an entity the member-level declaration still records intent for the event graph below, which
is where `PRAG0816` reads its origin (`RecallDrugAction.Execute()`).

The Actions transforms collect the declared events into the action's model, and the generated invoker
gets a `CollectRaisedEvents` that constructs each one by matching the event's constructor parameters
by name against the operation's own input properties (`action.X`) or, on a mutation, against the
entity (`entity.X`). The `result` parameter is in the generated signature but is never referenced: a
constructor parameter that exists only on the result is filled with `default`, and no diagnostic says
so. `DomainActionInvoker` then dispatches them **after a successful commit**; the entity has no
behaviour of its own here. A failure while dispatching is logged and swallowed rather than turned into a thrown
error, because the work is already committed and re-throwing would make callers retry it.

On top of that, `EventGraphValidator` checks the resulting graph during composition and
`EventCycleAnalyzer` reports cycles: the event graph of the whole application is knowable at build
time, from declarations rather than from reading handler bodies.

The attribute's metadata name is registered in `shared/SourceGen/AttributeNames.cs`, which is what
lets the generator features intercept it with `ForAttributeWithMetadataName`.

> **Not the same attribute**: `[Raises<T>]` here is unrelated to `[RaisesEvent<T>]` in
> `Pragmatic.Persistence`, which goes on the enum values of a state machine. The names are one word
> apart and the two do different things.

## External references

Named here, described where they live:

- **`Pragmatic.SourceGenerator` → `PendingContractGenerator` / `PendingContractsTemplate`**: the
  standalone generator that turns pending endpoint bodies into assembly metadata.
- **`Pragmatic.Testing.SourceGenerator` → `ContractTestGenerator`**: generates endpoint contract
  tests in the test project and skips the ones marked pending.
- **`Pragmatic.SourceGenerator` → Lifecycle feature**: transform, template and diagnostics that
  turn `[Raises<T>]` on an entity into an actual raise at the lifecycle transition.
- **`Pragmatic.SourceGenerator` → `EventGraphValidator`, `EventCycleAnalyzer`**: validate the
  declared event graph and report cycles.
- **`Pragmatic.Abstractions/Events` → `EntityLifecycle`**: the enum supplying the `On` parameter.
