# State Machine

Entities with a status enum can enforce valid state transitions **at compile time** with
`[StateMachine<TEnum>]`. The source generator turns the enum's `[TransitionFrom]` declarations into
guarded transition methods, so an invalid transition is a typed error, never an unchecked status
assignment. The attribute itself is listed in [Attributes](03-attributes.md); this page is the full
guide. For how transitions fit a mutation, see the [Mutation Pipeline](16-mutation-pipeline.md).

## Declaration

Annotate the enum members with the states they may be reached *from*, mark the initial state, and put
`[StateMachine<TEnum>]` on the entity:

```csharp
public enum ReservationStatus
{
    [InitialState]
    Pending,

    [TransitionFrom(ReservationStatus.Pending)]
    Confirmed,

    [TransitionFrom(ReservationStatus.Confirmed)]
    PaymentReceived,

    [TransitionFrom(ReservationStatus.Confirmed)]
    [TransitionFrom(ReservationStatus.PaymentReceived)]
    CheckedIn,

    [TransitionFrom(ReservationStatus.CheckedIn)]
    CheckedOut,

    [TransitionFrom(ReservationStatus.Pending)]
    [TransitionFrom(ReservationStatus.Confirmed)]
    [TransitionFrom(ReservationStatus.PaymentReceived)]
    Cancelled,

    [TransitionFrom(ReservationStatus.Pending)]
    [TransitionFrom(ReservationStatus.Confirmed)]
    NoShow
}

[Entity]
[StateMachine<ReservationStatus>]
public partial class Reservation : IEntity
{
    public ReservationStatus Status { get; private set; } = ReservationStatus.Pending;
}
```

## Generated members

The source generator produces, on the entity:

- `TransitionTo(newState)`: returns `VoidResult<IError>`; on refusal the error is a
  `Pragmatic.Result.Http.ConflictError` (status 409) naming both states
- `CanTransitionTo(newState)`: returns `bool`
- `AllowedTransitions()`: a **method**, returning `ReadOnlySpan<TEnum>` of the states reachable from here

The `Status` property is yours to declare; name it in the attribute when it is not called `Status`
(`[StateMachine<T>(Property = nameof(Phase))]`), or the generator reports **PRAG0623**.

A transition can also be refused by your own code: declare a parameterless `CanEnter{State}()`
returning `bool` and the generated `TransitionTo` calls it before moving.

## Usage in domain logic

```csharp
public VoidResult<IError> Confirm()
{
    var result = TransitionTo(ReservationStatus.Confirmed);
    if (result.IsSuccess)
    {
        RaiseEvent(new ReservationConfirmed(Id, GuestId, PropertyId));
    }
    return result;
}
```

Invalid transitions return a typed error rather than throwing; handle it like any other
[Result](../../Pragmatic.Result/README.md) failure. Because the transition graph is fixed at compile
time, adding a new state or edge is a one-line enum change, and any code path that would reach an
unreachable state simply cannot compile a valid `TransitionTo` for it.

## Usage in an operation: `[TransitionsTo]`

An operation that moves the state declares where to, and the generated invoker performs the move; the
body does not call `TransitionTo`:

```csharp
[Mutation(Mode = MutationMode.Update)]
[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed)]
public partial class ConfirmReservationMutation : Mutation<Reservation>
{
    public required Guid Id { get; init; }
}
```

A move the state machine refuses answers **409**, and the generated endpoint documents it. On a domain
action the entity moved is the one loaded with `[LoadEntity]` whose `[StateMachine<TState>]` matches.
`When` says when the invoker moves it:

| `When` | The invoker | Use it when |
|---|---|---|
| `BeforeBody` (default) | moves the entity after the loads, before the body; a refusal skips the body | the body needs the new state, or must not run when the move is refused. The only choice that fits a domain action, which builds its response in the body |
| `AfterBody` | moves it after a successful body, before validation, invariants and the save | the body has refusals of its own that must keep their own code (`NotYourRequestError`, not a generic 409). Mutations only: **PRAG0467** on an action |
| `ByBody` | does not move it; after a successful body, throws if the state is not the declared one | the move happens inside a domain method that writes the state together with who, when and why (`invoice.Void(reason, on)`) |

`IsConditional = true` with `ByBody` drops the check, for a move the body makes only sometimes: a payment
that settles the invoice, not every payment.

⚠️ With `BeforeBody` or `AfterBody`, a body that still calls `TransitionTo(target)` is **PRAG0466**: the
second call is a move from the target to itself, refused on every request. It was the only correct form
before, so an older operation has the call: remove it, or declare `ByBody` if the body is where the move
belongs. The other declarations the invoker cannot perform: **PRAG0465** (no entity with that state machine,
or more than one loaded), **PRAG0468** (on a mutation that is not an `Update`).

## Related

- [Attributes](03-attributes.md): `[StateMachine<T>]`, `[TransitionFrom]`, `[InitialState]`
- [Mutation Pipeline](16-mutation-pipeline.md): where transitions run within a mutation
- [Common Mistakes](common-mistakes.md): transition pitfalls
