# Contract Tests

The generator reads the contracts your app declares and emits xUnit tests that hold them to it.
This page states exactly what each family asserts and, just as importantly, what it does not.

Tests are grouped per boundary and land in `Pragmatic.Tests.Generated`.

## Authorization contracts

**When you get them.** For every `[Endpoint]` that requires a permission.

**What is generated.**

| Test | Assertion |
|------|-----------|
| `{Action}_WithoutRequiredPermission_IsRejected` | a caller holding **no** permission does not get through (4xx) |
| `{Action}_WithRequiredPermission_IsReachable` | a caller holding exactly the required permission is not blocked by authorization, **and is answered differently** from a caller without it |
| `{Action}_WithUnknownId_IsNotFound` | for a GET-by-id, an unknown id yields 404 |

The unprivileged caller is built with `AsUser(request, "contract-noperm")`, which writes an **empty**
permission header. That is deliberate: `HttpClient` copies its `DefaultRequestHeaders` onto any request
that does not already carry them, so merely omitting the header would let the fixture's grant (often
`*`) authorize the call, and the test would pass while proving nothing.

**Why the assertions are wider than "403" and "2xx".** Pragmatic's pipeline can reject a request before
the authorization filter runs: body binding answers 400, an entity lookup answers 404. Requiring a
literal 403 would produce false failures on endpoints that legitimately reject earlier. Conversely an
authorized call may still end in 404 because the id is random, so the positive contract asserts only
that authorization did not block it.

The consequence is worth stating plainly: a rejection test passes on any 4xx, so it confirms *the caller
did not succeed* rather than *the authorization filter ran*. For endpoints where the distinction matters,
add a hand-written test asserting the exact status.

⚠️ **Which is why the reachable test sends both callers.** "Not forbidden" is satisfied by "never got
there": when something refuses every request before it reaches a route (a tenant middleware answering
400, a header the host requires and the test does not send), the unprivileged call is a 400, which
counts as rejected, and the privileged call is the same 400, which counts as not forbidden. Both halves
pass and nothing has been measured: a whole generated suite can report success while every request is
being refused.

So the reachable test issues the unprivileged request too and asserts, with
`ShouldBeAuthorizedUnlike`, that the two were **not answered the same way**. The legitimate outcomes all
differ: 403 against 404 for a read of a random id, 403 against 400 for a create the shape cannot carry,
403 against 2xx for one it can. Two identical statuses mean the permission changed nothing observable.

**List endpoints are skipped.** A GET without a route parameter data-scopes rather than rejecting: it
returns 200 with filtered results, so asserting a 4xx there would be wrong. Visibility for those is
covered by scope tests, not by authorization contracts.

## CRUD contracts

**When you get them.** For every create endpoint whose request body the generator can synthesize.

| Test | Assertion |
|------|-----------|
| create with a synthesized body | 2xx |
| create with an empty body | 400: validation rejects it |
| cross-tenant read | an entity created under one tenant is 404 for another |

Bodies come from `TestDataSynthesizer`: strings are unique (`"test-" + Guid`), numbers in range, an enum
takes its first member, ids and timestamps come from the clock. Foreign keys and nested complex types are
**not** synthesizable: when one appears, the success test is skipped and the validation test is kept,
rather than asserting 2xx against a body that cannot be valid.

Entities are ordered by `FkTopologicalSorter` so parents are created before children; circular foreign
keys are detected and do not hang the generation.

## What the generator cannot know, and where you tell it

⚠️ The generator fills a request from the operation's **shape** and invents an identity carrying the
required permission. Neither is always enough, and when it is not, the generated test fails on its own
assumption rather than on a defect in the application:

- **400** for a create whose validity needs more than the shape: a value that must already exist, a pair
  that must agree, a name that must be unique.
- **403**: a permission is not always the whole authority. A tenancy operation needs a caller with a
  tenant. A host that calls `UseAuthorization(authz => …)` installs a resolver that derives permissions
  from roles and **stops honouring raw permission claims**, so a caller carrying the exact permission is
  still refused.

Both are host wiring rather than module metadata, so the generator cannot see either. It hands over
instead, at the seam your fixture already writes to:

```csharp
public sealed class ContractAppFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        PragmaticContractHost.Client = _app.CreateClient();

        // A body for the creates the shape cannot carry. null for everything else.
        PragmaticContractHost.BodyFor = operation => operation switch
        {
            "CreateWorkspace" => new { Name = $"ws-{Guid.NewGuid():N}", OwnerId = _seededOwnerId },
            _ => null
        };

        // The last word on every contract request, after the generated identity is written.
        PragmaticContractHost.PrepareRequest = contract =>
        {
            contract.Message.Headers.TryAddWithoutValidation("X-Tenant-Id", "tenant-a");
            contract.Message.Headers.TryAddWithoutValidation("X-User-Roles", "workspace-admin");
        };
    }
}
```

## What was **not** generated, and why

A generated suite reports what it wrote and never what it skipped, so "eight operations are covered"
and "the application publishes eight operations" read the same from inside it. `ContractCoverage`
(generated beside the tests) is the other half:

```csharp
foreach (var operation in Pragmatic.Tests.Generated.ContractCoverage.Operations)
    // Boundary, Operation, HttpMethod, Route,
    // Contracts:  ["auth", "create", "validation", "isolation"]  (what was emitted)
    // NotCovered: ["create and isolation: it takes a multipart body, …"] (and why not)

Pragmatic.Tests.Generated.ContractCoverage.Uncovered   // the operations that got nothing at all
```

Pin it in a test. A new operation that nobody contracts then fails that test instead of joining the
silence:

```csharp
[Fact]
public void NoOperationIsLeftWithoutAContract()
    => ContractCoverage.Uncovered.Should().BeEmpty();
```

⚠️ **The commonest reason for "nothing" is no permission.** An authorization contract asserts that a
caller *without* the permission is refused and one *with* it gets further; an operation that declares
no `[RequirePermission]` refuses nobody, so there is nothing to assert and none is emitted. That is
correct, and it is also how four operations of a real application turned out to be callable by any
authenticated user; nobody had noticed, because the generated suite looked complete. The
other reasons are stated per row: a POST whose route carries a parameter acts on a resource that
already exists, a multipart body answers 415 to a JSON one, and a command's success is not a
generatable contract because a synthesised body may not meet preconditions the generator cannot read.

## An application that is more than one service

The generated classes are emitted **per boundary** (`IntakeAuthContractTests`,
`VerifyAuthContractTests`), and each one says which boundary it is. An application whose services are
separate processes registers a client per boundary instead of the single `Client`:

```csharp
PragmaticContractHost.UseClientFor("Intake", _intake.CreateClient());
PragmaticContractHost.UseClientFor("Verify", _verify.CreateClient());
```

The boundary is the `[Boundary]` class's name without its suffix, the same one the generated class is
called after. It also reaches `PrepareRequest`, which is what lets a fixture pick a signing key or a
role table per service:

```csharp
PragmaticContractHost.PrepareRequest = contract =>
    contract.Message.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer", contract.Boundary == "Verify" ? VerifysToken() : IntakesToken());
```

⚠️ **Once one boundary has a client of its own, a boundary with none is an error and not a fallback.**
That is deliberate: sending a second service's contracts to the first one's host answers **404**, and a
404 is an ordinary contract outcome, so the suite would report a failure about a service that never
saw the request. The message names the boundary that is missing and the ones that are registered.

An application of one service changes nothing: with no `UseClientFor` call, every boundary gets
`Client`.

⚠️ Without this seam the only thing a request carries that hints at which service it belongs to is its
**path**, and routing by route prefix sends a route added to the second service under a prefix the table
does not name to the first one, where it fails as a contract error about the wrong host.

**The operation name is the endpoint's own action name**, whichever contract is asking:
`CreateWorkspaceMutation`, `GrantRolePermissionMutation`, `IssueInvoiceAction`: the class name without
an `Endpoint` suffix, which is also what the generated method names are built from. One endpoint, one
key: the auth contract, the CRUD round-trip and the create step of a state-transition flow all ask
under it, so an application answers once. Returning `null` from `BodyFor` keeps the synthesised body,
which is what most creates want.

⚠️ One key, not one per contract: with a key each, a fixture answers for whichever it was written
against, and the other contracts post the synthesised body and fail on the arrangement, which says
nothing about what they exist to measure. A state transition keeps a key of its own, `{Entity}_TransitionTo{TargetState}`, because it is a
different request from the endpoint's auth contract.

**Neither hook reduces what is generated.** Every contract the generator emitted before it still gets
emitted; the hooks change what the test sends, not whether the test exists.

**A create the shape cannot carry gets its tests too, and asks you for the body.** A required member
the synthesiser cannot invent (a foreign key, or a nested collection of another mutation, which is how
an aggregate that carries its children is written) leaves it with no body to post. The create and the
tenant-isolation test are emitted all the same, and fail with a message naming the operation:

```
The contract test for 'CreateDraftInvoiceMutation' needs a body from the application: the
generator could not fill one from the operation's shape … Set PragmaticContractHost.BodyFor …
```

Answer it in the fixture, where a row the body needs can be seeded first. ⚠️ Not skipped: a suite
that emits one test where it could emit three looks exactly like a suite that passes, and `BodyFor`
could not rescue tests that are missing, because it is read *inside* them.

## State-transition contracts

**When you get them.** For endpoints marked `[TransitionsTo]`, correlated with the create endpoint of
the same entity.

| Test | Assertion |
|------|-----------|
| legal transition from the initial state | 2xx |
| illegal transition from the initial state | 409 Conflict |
| the other answer, from a state the entity's other transitions reach | 409, or 2xx |

Every transition gets both answers. The one the initial state cannot give is reached by **walking**: the
generator knows every transition endpoint of the entity and every target's `[TransitionFrom]` set, finds
the shortest chain of real transitions to a state where the opposite answer is due, POSTs each step with
its own body and permission, then the transition. Issuing an invoice is legal from `Draft`: its other half
issues once and issues again, expecting 409. Paying is illegal from `Draft`: its other half issues first,
then pays, expecting 2xx. A state no declared transition reaches gives no test for that half, and says so
at build time (`PRAG2363`, Info) instead of emitting a skipped placeholder that a suite counts and nobody
reads.

**`[TransitionsTo]` is the only way a transition is visible.** An operation that moves an entity
without declaring it (a `TransitionTo(...)` left in a body, which the generator cannot read across
assembly metadata) produces **no contract and no placeholder**, and the machine looks covered because
nobody is looking. Declare it on every operation that moves the entity, whatever kind of operation it
is: `Pragmatic.Actions.Attributes.TransitionsToAttribute<TState>` goes on a mutation and on a
`DomainAction` alike. The declaration is also what performs the move: the generated invoker runs it,
unless `When = TransitionTiming.ByBody` leaves it to a domain method and checks the result, so the
contract and the behaviour read the same line.

**They need the entity in its initial state.** A transition test creates it first, so it is generated
against a create endpoint correlated by entity. That create may be a `Mutation<TEntity>` **or** a
`DomainAction` that creates the entity; the latter gets no CRUD contract of its own, being a command,
but it is recorded precisely so it can serve as the arrange step here, and for an entity with a state
machine it is often the only way one is ever created.

A body the shape cannot fill is **not** a reason to skip: the create goes through
`PragmaticContractHost.Body` like any other, and the application supplies it.

**An entity with no create route is arranged by the application.** An invoice a confirmed booking raises,
a verification a case asks for across a broker: nothing over HTTP creates them, so the contract asks
`PragmaticContractHost.ArrangeFor`: given the entity's name and its boundary's client, bring one into its
initial state and return its id. Without it the test fails naming what to set, rather than being skipped:

```csharp
PragmaticContractHost.ArrangeFor = async (entity, client) => entity switch
{
    "Invoice" => (await ConfirmAReservationAndFindItsInvoiceAsync(client)).ToString(),
    _ => null,
};
```

**The transition posts its own body too**, read from its endpoint exactly as a create's is: every
public settable member that is not a route token, a `[FromQuery]`/`[FromHeader]` parameter, or a value
the invoker fills (`[FromClock]`, `[FromCurrentUser]`, `[FromClaim]`). It travels through
`PragmaticContractHost.Body` under the transition's **own** operation name,
`{Entity}_TransitionTo{TargetState}`, so `BodyFor` can replace it: voiding an invoice needs a reason,
which the synthesiser fills; recording a payment needs a `Money`, which it cannot invent, and the
application gives that one under that name.

A transition that takes nothing but its route posts **no content at all**, which is the majority of
them. That distinction matters in both directions: an endpoint that requires a body answers 415 before
it can answer the 409 or the 2xx the contract measures, and an endpoint that takes none is sent a `{}`
nobody would send.

⚠️ A member bound from the route by name (`public required Guid Id { get; init; }` with no
`[FromRoute]`, the usual shape for an operation that loads by id) is recognised from the route's
`{tokens}`, not from an attribute. Read as a body member it would be a required `Guid` ending in `Id`,
which the synthesiser refuses to invent as a foreign key, and the whole body would come out
unsynthesisable.

## Typed client

`_Api.{Boundary}.g.cs` is not a test: it is the client the *hand-written* tests use. See
[Typed test client](typed-client.md).

## What contract tests do not cover

They are a floor, not a ceiling. They assert the contract the metadata describes; they say nothing about
business rules, computed results, or side effects. Specifically out of scope:

- that a rejection is a *403* rather than some other 4xx (see above);
- response payloads beyond their status code;
- anything involving more than one request, other than the CRUD round-trip and transition flows;
- permissions that are enforced somewhere other than the endpoint.

Write those by hand with the typed client.
