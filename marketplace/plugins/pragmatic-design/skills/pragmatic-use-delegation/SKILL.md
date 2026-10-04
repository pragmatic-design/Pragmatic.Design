---
name: pragmatic-use-delegation
description: Use when a session acts for someone else (an agent for its user, support for a customer, a job for a row's owner) or a permission check differs under impersonation; ICurrentUser.Delegation, delegation policies, RFC 8693.
---

# Pragmatic Use Delegation

**Covers:** Act on behalf of another identity with Pragmatic.Authorization: ICurrentUser.Delegation, the Intersection/SubjectOnly/GrantScoped policies, IActorAuthorityResolver, RFC 8693 claims.

A delegated session is one where **someone acts on behalf of someone else**. The subject is who the
work is *for*; the actor is who is *doing* it. Both are real, both belong in the audit trail, and the
authority is neither one's alone.

## When to use

- An agent runtime executes work started by a user.
- Support or an administrator reproduces what a customer sees.
- A background job acts for the owner of a row instead of running with full permissions.
- A colleague covers for someone who is away.

**Not** for an autonomous process that works for itself. That is a service principal with its own
permissions, which `Pragmatic.Identity` already expresses; inventing a subject to satisfy this API
makes the audit trail lie.

## The shape

```csharp
if (currentUser.Delegation is { } d)
    logger.ActedOnBehalf(currentUser.Id, d.ActorId, d.Purpose);
```

`IDelegationContext`: `SubjectId`, `ActorId`, `ActorKind` (`User|Service|Agent`), `Policy`, `Purpose`,
`GrantId`, `ExpiresAt`, `Chain`.

**`ICurrentUser.Id` is the subject.** Deliberately: row ownership is stamped with them, ownership and
scope filters select their rows, culture and preferences are theirs. Every piece of code that never
heard of delegation keeps doing the right thing, *for the person the work is for*, which is nearly
always the intention. Ask `Delegation.ActorId` when you need the executor.

## The three policies

The authority is composed, and the composition is inherited by every `[RequirePermission]`,
permission-based query filter and `ResourcePolicy` in the application, without any of them knowing.

| `Policy` | Effective permissions | Use it for |
|---|---|---|
| `Intersection` *(default)* | subject ∩ actor | an agent that must exceed neither side |
| `SubjectOnly` | the subject's, whole | support reproducing what a customer sees, limits included |
| `GrantScoped` | subject ∩ what the grant names | a job or service, whose own permissions are not user-shaped |

⚠️ **`Intersection` is the wrong default for a background job.** A job's identity holds no user-level
permissions, so the intersection is empty and the job can do nothing for anybody. Jobs want
`GrantScoped`. The policy travels **with each delegation**, not with the deployment, exactly so an
agent and a job can differ in the same application.

⚠️ **`SubjectOnly` hands over everything the subject can do**, including changing their own password.
Nothing in the framework holds that back today: the never-delegable list is not built yet. Use it
only where the actor is already trusted with the subject's account.

## When a delegation is refused

Expired, chain deeper than `MaxDelegationChainDepth` (default 2), or actor and subject in different
tenants with `AllowCrossTenantDelegation` off.

⚠️ **A refused delegation authorises nothing.** It does *not* fall back to the subject's own authority:
the request is being made by the actor, so falling back would hand it **more** than the delegation it
was refused would have given: an expired grant would become an upgrade.

Cross-tenant is off because nothing else defines which tenant a delegated session belongs to. Turn it
on only for a genuine cross-tenant support flow:

```csharp
// AuthorizationOptions (Pragmatic.Authorization.Configuration), not on the UseAuthorization builder
services.Configure<AuthorizationOptions>(o =>
{
    o.AllowCrossTenantDelegation = true;
    o.MaxDelegationChainDepth = 2;
});
```

## Where the authority comes from

`IActorAuthorityResolver` answers what the *actor* is allowed, which `IUserAuthorization` cannot: under
delegation that one answers for the subject.

`ClaimsActorAuthorityResolver` is the default and reads it off the token: right for a token-exchanged
delegation, wrong for jobs and agents whose authority lives in a service-account store or a grant
table. Register your own:

```csharp
services.AddScoped<IActorAuthorityResolver, MyActorAuthorityResolver>();
```

⚠️ **Absent claims mean no authority, not full authority.** Under `Intersection` a missing `act_perms`
makes the delegation powerless rather than unlimited. That is the correct direction: an issuer that
did not say must never read as "everything".

## Starting one from code: `IDelegationService`

For work with no HTTP request behind it: background jobs, event handlers, agent runners, seeds.

```csharp
await using var scope = ...;                       // your unit of work
using var _ = delegation.ActAs(
    subjectId: order.OwnerId,
    purpose: "nightly digest",
    policy: DelegationPolicy.GrantScoped);

// inside the using: ICurrentUser.Id is the subject, the authority is composed,
// and ownership stamping, row filters, cache keys and audit all follow.
```

**The actor is the caller**, taken from `ICurrentUser` and not passed in: an API where the caller
names both parties lets it name an actor it is not. An unauthenticated context therefore cannot open
a scope at all: there would be no actor.

It is an `AsyncLocal`, so it flows into everything awaited inside the `using` and into nothing after
it, provided there is a `using`.

⚠️ **A request-borne delegation wins.** Opening a scope inside a request that already carries one does
not override it: the caller already said who they act for.

⚠️ **Nesting extends the chain, it does not replace it.** An agent that starts an agent is two hops,
and `MaxDelegationChainDepth` can refuse it. Were it replaced, the cap would be unreachable.

⚠️ **This is what replaces running background work as a full-permission identity.** `SystemUser` is
still there and still has every permission; the difference is that a scope says *for whom*, and
everything downstream follows from that instead of from "allow anything".

## Starting one from a domain action: `[StartsDelegation]`

When the action *is* the takeover: an agent picking up work, an operator handling a case for a
colleague. The subject is named by a property on the action, so it arrives with the request.

```csharp
[DomainAction]
[RequirePermission(WorkPermissions.WorkItem.Update)]     // who may run it at all
[StartsDelegation(nameof(ForMemberId), Purpose = "agent takeover")]
public partial class TakeOverWorkItemAction : DomainAction<Guid>
{
    public required Guid WorkItemId { get; init; }
    public required string ForMemberId { get; init; }     // the subject
}
```

The invoker opens the scope around the whole execution and closes it after, outermost, so the row
filters computed inside see the delegated user.

⚠️ **Do not write `ActAs` in the action body instead.** The body runs *after* authorization has
decided, so the action would be authorized as the caller and executed as the subject: the fail-open
delegation exists to close. This is also why the attribute generates the `using`: an `AsyncLocal`
whose scope is forgotten leaks into the next request on the same pooled thread.

⚠️ **The action's `[RequirePermission]` is still evaluated against the caller**, before the scope
opens. Who may open a delegation and what may be done inside one are different questions.

⚠️ **And that permission is the only gate.** With no grant store, whoever may run the action may run
it *for whoever they name in the subject property*. Gate the action accordingly, and do not put
`[StartsDelegation]` on something broadly permitted.

`PRAG0423` if the named property is missing or is not a string: the scope cannot be generated, and
generating nothing would leave the action running as the caller while claiming otherwise.

## On the wire

RFC 8693 (OAuth 2.0 Token Exchange) vocabulary: `act_sub` names the actor; `act_kind`, `act_policy`,
`act_purpose`, `act_grant`, `act_chain` qualify it; `act_perms` and `act_grant_perms` carry the actor's
own and the grant's permissions.

**In development and in tests** `UseDevelopmentIdentity()` reads the same delegation off `X-Act-*`
headers, beside the `X-User-*` that name the subject:

```
X-User-Id: u-ada                     X-Act-Id: agent-7          ← makes it a delegation
X-Act-Policy: Intersection           X-Act-Kind: Agent
X-Act-Permissions: knowledge.knowledge-item.read                ← the actor's own authority
```

⚠️ **`X-Act-Id` gates the rest.** A policy or a purpose with no actor emits nothing: half a delegation
is worse than none, because two pieces of code then disagree about whose authority is in force.

## Caching, and why it matters here

Three caches key on the effective authority rather than the identity: the resolved permission set,
query results where permission-based filters apply, and `[Cacheable]` action answers. Under delegation
each gains a segment naming the actor, the policy and the grant, and, for the query and action caches,
an imprint of the **effective** permission set, because a delegation can be refused (expired, a chain too
deep, an actor of another tenant) without any of its identifiers changing, and a refused delegation holds
no permission. Only under delegation, so an application that never delegates has byte-identical keys.

This is not an optimisation. Keying a delegated session's permissions under the subject's id alone
means it either reads the subject's full set (a privilege escalation) or writes the narrowed one and
the subject silently loses permissions afterwards.

## Not there yet

Deliberately listed, because assuming otherwise is how a hole gets believed closed:

- **The grant store**, so "who may act for whom" is still the application's to answer: `ActAs` checks
  that the caller is authenticated, and nothing else. With `[StartsDelegation]` that answer is the
  action's own permission, which is coarse: it says who may act for *someone*, never for *whom*.
- **The never-delegable list**: nothing is protected from `SubjectOnly`.
- **Minting a token that carries `act`**: the framework reads the claims, it does not write them.
- **Noticing a revocation mid-flight.** The cache tags `actor:` and `grant:` exist so an eviction can
  reach a running session, but nothing evicts them yet.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase example application (code
that compiles and that `Showcase.IntegrationTests` exercises) and kept identical to it by the gate: a
domain action that starts a delegation with `[StartsDelegation]`, and the test that shows whose the row
is afterwards.

`IDelegationService` (starting a delegation from code rather than from an action) is used by no tested
application yet, so there is no example of it here: the sections above are the reference.
