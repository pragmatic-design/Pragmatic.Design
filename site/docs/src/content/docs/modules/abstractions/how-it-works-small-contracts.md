---
title: "The single contracts: six folders that hold one type each"
description: "> **Scope**: six files, one per folder, in `src/Pragmatic.Abstractions/`."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/small-contracts.md
sidebar:
  order: 22
---
> **Scope**: six files, one per folder, in `src/Pragmatic.Abstractions/`.
> `Result/IError.cs` · `Specification/ISpecification.cs` · `Pipeline/ICallContext.cs` ·
> `Temporal/Clock/IClock.cs` · `Pagination/Page.cs` ·
> `Internationalization/Context/IGlobalizationContext.cs`
>
> **Not covered here**: the implementations. `Error`/`ValidationError` belong to
> `Pragmatic.Result`, `Specification<T>` to `Pragmatic.Specification`, `SystemClock`/`TestClock` to
> `Pragmatic.Temporal`, `ActionCallContext` to `Pragmatic.Actions`, `I18NContext` to
> `Pragmatic.Internationalization`. They are named where they matter, not opened.

Signatures are catalogued in [interfaces](/modules/abstractions/interfaces/): [Result](/modules/abstractions/interfaces/#1-result),
[Temporal](/modules/abstractions/interfaces/#7-temporal),
[Internationalization](/modules/abstractions/interfaces/#12-internationalization),
[Pipeline](/modules/abstractions/interfaces/#13-pipeline), [Specification](/modules/abstractions/interfaces/#22-specification),
[Pagination](/modules/abstractions/interfaces/#23-pagination).

## Why they are grouped

These six folders have one thing in common and nothing else: each contains exactly one public type,
and that type is the whole feature as far as Abstractions is concerned. Everything a caller can do
with it happens in another package.

That is the deliberate shape of this module. A folder here is not a feature; it is the seam where a
feature crosses an assembly boundary. `Temporal/Clock/` holds six properties and one method; the
scheduling, the cron parsing and the test clock live in `Pragmatic.Temporal`, which depends on
Abstractions rather than the reverse. The consequence worth internalising: **a module that needs to
speak about time, errors, or specifications takes a dependency on Abstractions only, and never on
the module that implements them.** That is what keeps the dependency graph from turning into a
cycle, and it is why these types are small enough to be boring.

The rest of this page takes them one at a time.

---

## `IError`: the shape every failure agrees on

`Code`, `StatusCode`, and two members with default implementations, `Title` and `Description`. Four
members, and they carry the entire error contract of the framework.

This is the most widely referenced type in the repository: it is named across roughly nineteen
modules: Actions, Result, Endpoints, Identity, Authorization, Composition, Client, Resilience,
Configuration, Persistence, Ensure, Validation, Storage, MultiTenancy, Migrations and Caching among
them, plus the source generator's diagnostics and models. One of those is thinner than it looks: in
Caching the only occurrence is a `MetadataReference` in the test project, and nothing under its
`src/` names the type. Anything that can fail returns something that implements it.

Two decisions explain why it stayed this small.

**Localization is not on the interface.** `Title` and `Description` are plain `string`, and the
localized text is resolved from `Code` at the serialization boundary: `IErrorMessageResolver` in
`Pragmatic.Result.AspNetCore`, consumed by `DefaultProblemDetailsFactory`, which calls
`ResolveTitle(error.Code, error)` and `Resolve(error.Code, error)` and writes the raw `Code` into
`Extensions["code"]`. `Pragmatic.Internationalization.AspNetCore` supplies the localized
implementation. Had localization members lived on `IError`, every error type in every module would
have had to know about cultures, and Abstractions would have needed a globalization dependency to
declare them.

**`StatusCode` is on the error, not on a mapping table.** The error knows what it means; a central
map from error type to HTTP status is a second place to update and a first place to forget. The cost
is that a non-HTTP consumer carries a number it ignores, a smaller price than a mapping that drifts.

For most error types you extend the `Error` abstract record in `Pragmatic.Result` rather than
implementing this interface; implementing it directly is for struct-based errors such as
`ValidationError`, where a record base class would mean boxing.

---

## `ISpecification<T>`: one predicate, two evaluation worlds

`ToExpression()` returns `Expression<Func<T, bool>>`; `IsSatisfiedBy(T)` answers for a single
instance. The point of the pair is that the *same* business rule can be pushed into SQL or applied
in memory, without being written twice and without the two versions drifting.

Inside Abstractions itself, the specification's only point of use is the repository contract:
`IReadRepository` takes an `ISpecification<TEntity>` in four of its methods: `FindAsync`,
`CountAsync`, `ExistsAsync`, `FirstOrDefaultAsync`. The folder does not tell that story on its own;
see [persistence-repository](/modules/abstractions/how-it-works-persistence-repository/) for the repository side.

Outside it, three consumers:

| Module | Use |
|---|---|
| `Pragmatic.Specification` | `Specification<T>` base class and the combinators in `SpecificationExtensions`. |
| `Pragmatic.SourceGenerator` | `RepositoryTemplate` emits the four generated repository signatures that mirror `IReadRepository`. |
| `Pragmatic.Persistence` | `QueryBuilder.WithFilter(ISpecification<TEntity>)`, the query-side entry point. |

One note on performance claims: `Specification<T>` caches a `Lazy<Func<T, bool>>` and compiles the
expression once, so `IsSatisfiedBy` on the base class does not re-walk the tree. That is a property
of the base class, not of the interface: a hand-written implementation of `ISpecification<T>` gets
whatever it writes.

---

## `ICallContext`: the system acting on its own behalf

Two members: `IsInternalCall`, and `EnterInternalCall()` returning a disposable that restores the
previous state, nesting included. When the flag is set, authorization filters skip permission
checks.

That sentence is alarming out of context, so here is the context. **The trust boundary is the
process, and it is a convention rather than a construction.** Nothing *outside* the process can enter
the mode (no header, claim, route or request body reaches this API), but nothing stops code *inside*
it either: `EnterInternalCall()` is public on a service registered scoped in the application
container, with no keying, no internal-only wrapper and no analyzer, so any handler, validator or
filter that injects `ICallContext` can enter the mode and skip the permission and policy checks. No
misuse exists in this repository; the invariant is simply stated, not enforced. The
enumeration below is what the framework does today, not what the type prevents. The only callers in
the repository are

- the generated boundary invokers: `BoundaryInterfaceTemplate.Registration.cs` and
  `.SubBoundaries.cs` in the source generator,
- the event dispatcher: `Pragmatic.Events/InMemoryEventDispatcher.cs`,
- the message bus: `Pragmatic.Messaging.Core/InMemoryMessageBus.cs` and the generated
  `HandlerPipelineTemplate`,
- the generated composite action invokers: `CompositeActionInvokerTemplate`, which enters the mode
  once around all the child mutations, because the composite is the authorization boundary and its
  own `[RequirePermission]` has already been enforced.

Readers go through `ICallContext` too: `PermissionAuthorizationFilter`, `PolicyEvaluationFilter`
and `MutationInvoker.Validation` resolve the interface, not the concrete `ActionCallContext`. A
reader that resolved the concrete type would leave an `ICallContext` substituted in DI written to by
every writer and read by none: it would fall back to a fresh `ActionCallContext` at depth zero and
enforce. Fail-closed, but silently, and a substitution that looks wired would do nothing.

All in-process. Code running inside the host is trusted by construction, so a public interface grants
it no authority it did not already have. What the mode buys is the correct answer to a real
question: when a domain event fires and a handler reacts, whose permissions should apply? The HTTP
user's are the wrong answer: they never asked for the reaction, and a system that must not be
blocked by them would otherwise need a fake privileged principal.

**Why it is public rather than internal to Actions.** Events and Messaging must be able to enter the
mode without depending on Actions. They call it conditionally (`_callContext?.EnterInternalCall()`)
because the only registration in the framework is in Actions:
`TryAddScoped<ICallContext>(sp => sp.GetRequiredService<ActionCallContext>())`. In an application
without Actions installed nothing is registered, the null-conditional call does nothing, and the
mode simply does not exist. That also explains the one surprise worth knowing: if `IsInternalCall`
is always `false`, the usual cause is that Actions is not in the application at all; see
[troubleshooting](/modules/abstractions/troubleshooting/).

---

## `IClock`: time as a dependency

`UtcNow`, `Now`, `UtcToday`, `Today`, `UtcTimeOfDay`, `TimeOfDay`, and `GetTimeProvider()`. The first
six exist so that calling code never has to convert; the seventh is the interop seam.

The interop goes both ways, and the wiring in `Pragmatic.Temporal` is the part worth reading:

```
TryAddSingleton<IClock>(SystemClock.Instance);
TryAddSingleton<TimeProvider>(sp => sp.GetRequiredService<IClock>().GetTimeProvider());
```

`SystemClock` is implemented over a `TimeProvider`, and `TimeProvider` is registered *from* the
clock. Substituting `TestClock` therefore moves BCL time as well: code written against
`TimeProvider` (including third-party libraries) sees the same frozen instant as code written
against `IClock`. Two abstractions, one source of truth, and no test that passes because half the
process kept using the wall clock.

Named consumers of the interface, representative rather than complete; it is taken as a dependency
across eight areas of the repository: `Pragmatic.Temporal` (`SystemClock`, `TestClock`, an analyzer
and its code fix that steer `DateTime.UtcNow` towards it, and the `TemporalContextMiddleware` /
`TemporalContextAccessor` pair in `Pragmatic.Temporal.AspNetCore`), `Pragmatic.Jobs` (seven files:
the scheduler, the processor, both stores, the recurring-job pair and the registration),
`Pragmatic.Identity` (all seven `Identity.Local` authentication actions, from `RegisterUser` to
`ChangePassword`, plus two stores in `Identity.Persistence`),
`Pragmatic.Testing` (mocking), the source generator (`TraitActionModelBuilder`, the four trait action
templates and `AttachmentPurgeJobTemplate`), and the Showcase: `CreateReservationAction`,
`CancelReservationMutation`, `PlanCheckoutFollowUpEndpoint` and `CreateInvoiceWithFeesAction`. The
scale is the point: **these seven members cannot be changed as a local edit.**

---

## `Page<T>`: the plain page

A sealed class with `Items`, `TotalCount`, `Number` (1-based), `PageSize`, and three computed members:
`TotalPages`, `HasPreviousPage`, `HasNextPage`. `Number`, not `Page`: C# refuses a member named after
its type.

The constructor validates rather than trusting: `items` non-null, `number >= 1`, `pageSize >= 0`,
`totalCount >= 0`. The reasoning is in the code: a null `Items` surfaces as a
`NullReferenceException` at first enumeration, which is usually a serializer or a view, far from
whoever built the page wrong. All four guards are covered by `PageTests`.

**Not `PagedResult`.** That is the simple name of the Persistence result of a paged query, and
sharing it would make a file importing both namespaces fail with CS0104:

| Type | Where | What it is for |
|---|---|---|
| `Pragmatic.Pagination.Page<T>` | Abstractions (this one) | Plain, transport-agnostic page. No Result semantics. |
| `Pragmatic.Persistence.Query.Results.PagedResult<T, TError>` | Persistence | Result-pattern pagination: success or failure with an error. |
| `Pragmatic.Persistence.Query.Results.PagedResult<T>` | Persistence | What the generated grid queries return. |

If you are consuming a generated query, you want a Persistence one; [common
mistakes](/modules/abstractions/common-mistakes/) §12 says why they differ. This one exists for a specific
structural reason: it is reached through `ToPagedDtoAsync()` in
`Pragmatic.Mapping.EFCore/Extensions/PaginationExtensions.cs`, and Mapping cannot depend on
Persistence. A page shape that both sides can name has to live below both of them.

---

## `IGlobalizationContext`: the read-only view of ambient culture

`Culture`, plus `TimeZone` and `CurrencyCode` with `null` defaults; the two optional members are
declared as defaulted so an implementation that only knows the culture is complete without writing
anything.

In `Pragmatic.Internationalization`, `I18NContext` implements it. The ambient state itself is held in
an `AsyncLocal` and read as `I18NContext.Current`, which the ASP.NET middleware populates per
request.

The interface is the narrow view of the same state for code that wants to depend on a contract rather
than on the I18n type, and it is registered: `AddPragmaticInternationalization` binds
`IGlobalizationContext` as **scoped**, resolving to the current context, so application code can
inject it. Scoped, not singleton, because the value it exposes is the culture of the request being
served.

`GlobalizationFormatter` is built from that registration rather than from a `CultureInfo` handed to
it separately, and is itself scoped, so one request formats consistently even if something changes
the thread's culture mid-flight, and the formatter and the contract can never disagree about which
culture is current.

## External references

Named here, described where they live:

- **`Pragmatic.Result.AspNetCore` → `IErrorMessageResolver`, `DefaultProblemDetailsFactory`**: turn
  an `IError` into an RFC 7807 response, resolving text from `Code`.
- **`Pragmatic.Internationalization.AspNetCore` → `LocalizedErrorMessageResolver`**: the localized
  implementation of that resolver.
- **`Pragmatic.Specification` → `Specification<T>`, `SpecificationExtensions`**: the base class with
  the cached compiled delegate, and the And/Or/Not combinators.
- **`Pragmatic.Persistence` → `QueryBuilder.WithFilter`**: where a specification enters a query.
- **`Pragmatic.Temporal` → `SystemClock`, `TestClock`**: the production and test implementations of
  `IClock`, and the registration that bridges `TimeProvider`.
- **`Pragmatic.Actions` → `ActionCallContext`**: the only `ICallContext` implementation, and the only
  registration.
- **`Pragmatic.Mapping.EFCore` → `PaginationExtensions.ToPagedDtoAsync()`**: projects and pages in
  one call, returning `Pragmatic.Pagination.Page<T>`.
- **`Pragmatic.Internationalization` → `I18NContext`, `GlobalizationFormatter`**: the ambient culture
  holder and the culture-aware formatter.
