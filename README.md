# Pragmatic.Design

[![CI](https://github.com/pragmatic-design/Pragmatic.Design/actions/workflows/ci.yml/badge.svg)](https://github.com/pragmatic-design/Pragmatic.Design/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)](https://dotnet.microsoft.com)
[![License: MIT + PolyForm SB](https://img.shields.io/badge/license-MIT%20%2B%20PolyForm%20SB-blue.svg)](docs/LICENSING.md)
[![Preview](https://img.shields.io/badge/status-1.0.0--alpha-ffe088)](docs/ROADMAP.md)

> *One generator to rule them all, one generator to find them,*
> *one generator to bring them all — and at compile time bind them.*

**Describe your domain. One source generator writes the application around it.**

Pragmatic.Design is a .NET 10 framework for line-of-business software. You write what is specific to
your business — entities, the operations on them, the rules they obey — and a single incremental
source generator writes what is not: persistence, HTTP endpoints, validation, authorization, events,
caching, DI wiring, OpenAPI. Two companion generators go further, and write a typed HTTP client for
your API and the tests of its contract.

It generates **plain C# you can read** in `obj/`, it **refuses at compile time** what would fail at
runtime, and the code it writes into your project **is yours**.

> [!TIP]
> **Start here with your agent.** Give your coding agent the Pragmatic skills from
> **[pragmatic-design/skills](https://github.com/pragmatic-design/skills)**, then describe the application
> you want: `pragmatic-new-app` asks what it needs — the bounded contexts, who calls the API, whether rows
> belong to a tenant, which capabilities — and scaffolds the solution from the packages. They install as
> a plugin in Claude Code and Codex, and work in any agent that reads `SKILL.md` —
> [Build with an agent](https://docs.pragmaticdesign.net/getting-started/with-an-agent/) has the steps for
> each.
>
> Writing the code yourself: [Installation](https://docs.pragmaticdesign.net/getting-started/installation/).

---

## One declaration, a whole slice

This is a real entity from the [Showcase](examples/showcase/), a hotel-booking reference application
(abridged; the comments are added here):

```csharp
[Entity]                              // identity, Create() factory, typed setters, repository, EF mapping
[Auditable]                           // CreatedAt/By, UpdatedAt/By, stamped on every save
[SoftDelete]                          // IsDeleted and a global query filter
[HasOwner]                            // every user sees their own rows; a bypass permission sees all
[ConcurrencyAware]                    // optimistic concurrency (xmin on PostgreSQL, rowversion on SQL Server)
[StateMachine<ReservationStatus>]     // status transitions guarded at compile time
[Raises<ReservationCreated>]          // a domain event when a reservation is created
[Resource("reservations", Capabilities = ResourceCapabilities.Read | ResourceCapabilities.List)]
[HasComments]                         // comments: entity, actions and endpoints, for this type
[HasTags]                             // tagging, likewise
[HasAttachments(
    AllowedExtensions = ".pdf,.jpg,.png,.docx",
    PurgeDeletedAfterDays = 30,
    ThumbnailMaxWidth = 200,
    ThumbnailMaxHeight = 200)]        // uploads, thumbnails at upload, retention purge
[Relation.ManyToOne<Guest>]           // foreign key and navigation
public partial class Reservation : DomainEventSource, IEntity
{
    [GeneratedValue("RES-{YYYY}{MM}-{SEQ:5}")]   // RES-202609-00001, backed by a database sequence
    public string ReservationNumber { get; private set; } = "";

    [FutureDate]
    public DateTimeOffset CheckIn { get; private set; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; private set; }

    [Projectable]                                // usable inside SQL filters and projections
    public int NightsCount => (CheckOut - CheckIn).Days;

    public ReservationStatus Status { get; private set; } = ReservationStatus.Pending;
    // …
}
```

An operation is a class too. This one is an HTTP endpoint, checks a permission, loads the reservation,
runs the transition, commits, and answers with a documented `409` when the state machine says no — with
no body, because every one of those steps is declared:

```csharp
[Endpoint(HttpVerb.Post, "/{id}/confirm")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Update)]
[Mutation(Mode = MutationMode.Update)]
[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed)]
public partial class ConfirmReservationMutation : Mutation<Reservation, ConflictError>
{
    public required Guid Id { get; init; }
}
```

A body is for what is not declarable — a rule of your own, a domain method writing more than the state.
`[TransitionsTo]` then says whether the move happens before the body, after it, or inside it.

And a read is a declaration with no body at all — filters, sorting and a projection computed in SQL
(abridged):

```csharp
[Query<Reservation, ReservationSummaryDto>]
[RequirePermission(BookingPermissions.Reservation.Read)]
[Endpoint(HttpVerb.Get, "api/reservations/search")]
public partial class SearchReservationsQuery
{
    [Filter] public Guid? GuestId { get; init; }
    [Filter] public ReservationStatus? Status { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "CheckIn")]
    public DateTimeOffset? FromDate { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? CheckInSort { get; init; }
}
```

---

## What the generator writes for you

Those three classes are all the Showcase writes for them. Build it, and this is what appears in
`obj/` — counted on a fresh build of the booking module:

| From | Generated | Files |
|---|---|---|
| `Reservation` | Its own partials: the `Create()` factory, typed setters, the repository, relations and includes, the SQL projections of `NightsCount`, the state machine, the soft-delete and ownership filters, the lifecycle event, the `RES-…` number generator, the validator of `[FutureDate]` and `[GreaterThanProperty]` | 22 |
| `[HasComments]` `[HasTags]` `[HasAttachments]` | Three child entities with their EF configuration, DTOs and a filter that shows each child only where its reservation is visible; **14 endpoints** — add, read, edit and delete a comment, add and remove a tag, upload, download, preview and delete an attachment, list each — every one with its invoker; the job that purges deleted attachments after 30 days | 71 |
| `[Resource(…)]` | A read and a list query with their invokers, two DTOs with their mappings, **2 endpoints** | 14 |
| `ConfirmReservationMutation` | The endpoint, the mutation invoker, the request mapping, the validator and its metadata | 5 |
| `SearchReservationsQuery` | The query, its invoker, the endpoint | 3 |

That is **115 files from three classes**. In the host, the generator adds what only the host can
know: the EF Core configuration of every entity, one DbContext per database, the DI registration of
every module and the route table.

Every file is ordinary C#. This is the confirm endpoint — an excerpt, with namespaces shortened:

```csharp
// ConfirmReservationMutation.Endpoint.g.cs
var builder = endpoints.MapPost("/{id}/confirm", (RequestDelegate)(async httpContext =>
{
    var __raw_id = RequestValues.Route(httpContext, "id");
    if (!RequestBinder.TryBind<Guid>(__raw_id, out var id))
    {
        await BindingFailure.WriteAsync(httpContext, "id", "the value is missing or malformed");
        return;
    }
    var invoker = httpContext.RequestServices
        .GetRequiredService<IMutationInvoker<ConfirmReservationMutation, Reservation>>();

    var __result = await handler(id, invoker, httpContext, httpContext.RequestAborted);
    await __result.ExecuteAsync(httpContext);
}));
builder.RequireAuthorization(policy => policy.AddRequirements(
    new PragmaticPermissionRequirement(new[] { "booking.reservation.update" }, PermissionMode.All)));
builder.WithMetadata(new ProducesResponseTypeMetadata(409, typeof(ProblemDetails), new[] { "application/problem+json" }));
// … 200, 400, 401, 403, 404, 422 and 500 likewise
```

The binding is typed, the permission is the one you declared, the `409` is the `ConflictError` you
declared. To read your own, add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to
the project.

---

## What it believes

**Declare intent; let the compiler write the plumbing.** Infrastructure code is mechanical, and
mechanical code written by hand drifts from the thing it serves. Written by a generator from one
declaration, it cannot.

**What the compiler knows is not looked up at runtime.** If the generator can decide something at
compile time, it emits the line that does it — not a `GetService` that might return null, not an `is`
check that might never match. A branch that is never taken is indistinguishable from one that works;
generated code that says exactly what happens has no such branches. The generated path uses no
reflection.

**The module declares, the host composes.** Each module states facts about itself — its entities,
its operations, its endpoints, its permissions. The host, the one place that sees every referenced
assembly, puts them together: DI, routing, databases, dispatch. The Showcase runs the same
modules as one host and as two.

**Composition by presence.** Reference a package and its feature switches on; remove it and the code
it generated disappears. The host's `.csproj` is the list of what your application does.

**Failures are values.** Operations return `Result<T, TError>`, not exceptions. The errors an
operation declares become its HTTP statuses, its OpenAPI responses and its client's types.

**Mistakes are diagnostics.** Anything the generator can see is wrong, it says — at build time, with
an ID and a location.

**You own the output.** The generated code lands in your project and compiles without the generator.
No lock-in, no runtime you cannot read.

---

## The compiler is on your side

The generator does not just emit code; it understands what you declared, and it tells you when two
declarations disagree. Two warnings from a real application built on the packages:

```text
warning PRAG0688: 'KnowledgeItem' declares [Auditable] and [ConcurrencyAware], which is applied at
SaveChanges. ExecuteUpdate goes straight to SQL without passing the change tracker, so the audit
columns are not stamped and the concurrency token does not move. Use the repository, or accept the
trade deliberately.

warning PRAG0430: 'HandOverTermStewardshipAction' is a [CompositeAction] and declares
[CommitStrategy(CommitMode.PerStep)]. A composite commits once by construction and the attribute
has no effect. Remove it, or drop [CompositeAction] and compose in the body, where PerStep is honoured.
```

There are over **400** of these, each in the [diagnostics dictionary](docs/diagnostics.md) — the
kind of mistake that otherwise surfaces in production, or in a code review if you are lucky.

---

## Why a generator, when an agent can write the code?

Because repetitive code is not only tedious for people. It is where agents are weakest too, and the
cost moves rather than disappears.

An agent will happily write the repository, the endpoint, the validator and the registration for
your entity — and again for the next one, a little differently each time. Every copy is code you own,
and code you have to review before you trust it: plumbing does not stop being plumbing because a
machine typed it. It just arrives faster than anyone can read it. The 115 files above are 115 files
to check, for every slice of the application, every time the model has a different idea.

A generator writes them the same way every time, from one implementation that is tested once, for
everybody, by the suite described [below](#how-we-know-it-works). What is left to review is the part
that is actually yours — the declaration and the domain logic — and that is a few dozen lines, not a
few hundred.

It also makes the agent better at the part that is left:

- **The context is small.** A declaration is a dozen typed lines. An agent reads it, and so do you.
- **The feedback is immediate.** When an agent declares something that does not fit together, a
  diagnostic says so at build time, with a location — a loop the agent can close on its own, before
  a person looks at anything.
- **The framework can be taught.** The repository ships [35 agent skills](marketplace/), for Claude
  Code, Codex and any agent that reads `SKILL.md`: one asks what the application needs and scaffolds
  the solution, the others know each module.

You still read what the agent wrote. There is just much less of it, and none of it is plumbing.

---

## What's in the box

45 modules, one generator with 31 feature pipelines. Adopt one library or the whole stack.

| Area | What you get |
|---|---|
| **Domain & data** | Entities with identity, audit, soft delete, relations, state machines, hierarchies and temporal relations; repositories; declarative queries with filters, sorting, paging and SQL projections; declarative schema migrations for SQLite, PostgreSQL and SQL Server |
| **Operations & HTTP** | Mutations, queries and domain actions with a generated pipeline; minimal-API endpoints with OpenAPI, versioning and typed errors; PATCH with tri-state semantics; typed HTTP clients generated from the API manifest |
| **Security** | Identity (local, JWT, Keycloak, OIDC); roles, groups, permission wildcards and resource policies; row ownership and access scopes; multi-tenancy, shared-schema or database-per-tenant; encryption at rest with key rotation |
| **Compliance** | GDPR erasure, retention, consent, access and portability; an append-only audit trail that can be verified; NIS2 incident reporting clocks; personal-data redaction in logs and audit |
| **Async** | Domain events with a transactional outbox; messaging over Channels, RabbitMQ, Kafka or Azure Service Bus, with sagas; background jobs with retry, timeout and distributed locking; a cache with tag invalidation that crosses hosts over Redis |
| **Documents & media** | PDF, DOCX, XLSX, CSV and email templating; native (Rust) image resizing, conversion and QR codes; notifications by email, SMS and push; file storage on disk, S3, Azure, Google Cloud, FTP and SFTP |
| **Foundations** | `Result` and railway-style errors, guard clauses, source-generated validation and mapping, DST-aware time and business calendars, money and translations, resilience policies, configuration, feature flags — all usable on their own |
| **Testing** | Contract tests generated per endpoint (authorization, CRUD, state transitions), a typed test client, HTTP assertions and generated mocks |

The full list, module by module, is [below](#modules).

---

## Why not…

- **…hand-rolled Clean Architecture?** You stop writing the repository, the validator, the endpoint
  and the registration for every entity. They are generated, consistent, and never out of date with
  the entity they serve.
- **…MediatR + Mapperly + FluentValidation + EF Core, stitched together?** Those are good libraries
  that do not know about each other. Here the pieces are one design: an operation validates,
  authorizes, persists, raises its events and invalidates its cache from a single declaration, and
  the endpoint documents exactly the errors the operation declares.
- **…a heavyweight framework such as ABP?** Opinionated, but not a black box: every generated file is
  readable C#, the wiring is decided at compile time, the generated code does not reflect (what that
  means for Native AOT is [stated below](#how-we-know-it-works)), and you can take a single library
  without the rest.

**It is probably not for you (yet)** if you need a stable 1.0 today, depend on an ecosystem that is
not first-class here, or cannot run .NET 10.

---

## How we know it works

A framework that writes your infrastructure has to be more reliable than the code it replaces. This
is what every change goes through — one script, [`scripts/check.mjs`](docs/TESTING.md), the same on a
laptop and in CI:

- **16,964 tests**, measured on the last full run: 13,961 hermetic, and 3,003 against the real thing
  in containers — PostgreSQL, SQL Server, Redis, RabbitMQ, Kafka, Azure Service Bus.
- **Ratchets that only go down**: reflection in runtime source, trim and AOT warnings, generated code
  that reads the wall clock (zero). An SBOM (CycloneDX) and a vulnerability scan on every run.
- **A reference application**: the [Showcase](examples/showcase/) — three bounded contexts on
  PostgreSQL, run as one host and as two, exercised end to end over HTTP.

**Native AOT, precisely.** The code the generator emits does not reflect: serialization goes through
generated `JsonTypeInfo`, and generated endpoints are mapped as plain `RequestDelegate`s rather than
through ASP.NET's runtime delegate factory. Three smoke applications —
the serialization seam, a generated JSON context, and a web host serving a generated endpoint — are
published Native AOT and run by the gate. That is not the same as "every Pragmatic application is a
Native AOT application":

- **EF Core is not fully compatible with trimming and AOT**, and Pragmatic does not test it there — so
  a host with persistence is not a Native AOT application today.
- The generated HTTP client has no JSON context yet.
- Some runtime code is not trim-safe — reflective fallbacks, configuration binding. It is annotated,
  and the gate counts it: 194 trim/AOT warnings across Pragmatic assemblies, a number that is only
  allowed to fall.

**Maturity.** This is `1.0.0-alpha`. A core set is stable — Abstractions, Ensure, Result,
Specification, Mapping, Validation, Temporal, Storage and the core of Persistence; the other modules are
functional or in preview, the platform layer (Agent, Gateway) is experimental, and APIs can still change
before 1.0. What each word means is in [`docs/ROADMAP.md`](docs/ROADMAP.md), and each module's README
says where it stands.

---

## Getting started

> **The packages are not on nuget.org yet.** Until they are, the alpha builds are consumed from a
> local feed: [set one up](docs/howto/local-nuget-server.md) and `node scripts/publish-local.mjs`
> packs the repository into it.

Then pick your way in:

- **See it whole** — the [Showcase walkthrough](docs/howto/showcase-walkthrough.md) takes you through
  a complete application, module by module.
- **Build your first app** — the [step-by-step recipe](marketplace/plugins/pragmatic-design/skills/pragmatic-ecosystem/references/cookbook/crud-web-api.md)
  goes from an empty folder to a CRUD API on PostgreSQL, and every step of it has been built and run
  against the packages.
- **Build it with an agent** — [install the skills](https://docs.pragmaticdesign.net/getting-started/with-an-agent/)
  in Claude Code, Codex or another agent: one asks what the application needs and scaffolds the
  solution, the others know each module.
- **Just one library** — `Result`, `Validation`, `Mapping`, `Temporal` and the other foundations work
  in any .NET 10 project, with no host and no buy-in.

---

## Modules

<details>
<summary><b>All 45 modules</b></summary>

### Layer 0 — Foundation

| Module | Purpose |
|--------|---------|
| [Pragmatic.Result](Pragmatic.Result/README.md) | Zero-allocation Result types with railway-oriented programming |
| [Pragmatic.Ensure](Pragmatic.Ensure/README.md) | Guard clauses (ThrowIf, Is, Check patterns) |
| [Pragmatic.Abstractions](Pragmatic.Abstractions/README.md) | Pure interfaces shared across all modules |

### Layer 1 — Capabilities

| Module | Purpose |
|--------|---------|
| [Pragmatic.Validation](Pragmatic.Validation/README.md) | Source-generated validation from 30+ attributes, async validators |
| [Pragmatic.Mapping](Pragmatic.Mapping/README.md) | Source-generated entity/DTO mapping with EF Core projections |
| [Pragmatic.Specification](Pragmatic.Specification/README.md) | Composable query predicates for IQueryable and IEnumerable |
| [Pragmatic.Caching](Pragmatic.Caching/README.md) | Typed cache keys, tag invalidation, HybridCache integration |
| [Pragmatic.Temporal](Pragmatic.Temporal/README.md) | Type-safe dates/times, DST-aware arithmetic, business days, cron |
| [Pragmatic.Internationalization](Pragmatic.Internationalization/README.md) | i18n/l10n: Money, Currency, Formatters, Translation keys |
| [Pragmatic.Resilience](Pragmatic.Resilience/README.md) | Retry, circuit breaker, timeout with source-generated policies |
| [Pragmatic.Configuration](Pragmatic.Configuration/README.md) | Source-generated configuration binding with validation |
| [Pragmatic.Patch](Pragmatic.Patch/README.md) | Source-generated PATCH DTOs with tri-state semantics |

### Layer 2 — Integration

| Module | Purpose |
|--------|---------|
| [Pragmatic.Actions](Pragmatic.Actions/README.md) | CQRS-style domain actions with pipeline, filters, typed errors |
| [Pragmatic.Endpoints](Pragmatic.Endpoints/README.md) | Source-generated ASP.NET Core minimal API endpoints |
| [Pragmatic.Persistence](Pragmatic.Persistence/README.md) | Entity declarations, repositories, queries, mutations, filters |
| [Pragmatic.Persistence.EFCore](Pragmatic.Persistence/README.md) | EF Core runtime: DbContext generation, interceptors, bulk ops |
| [Pragmatic.Events](Pragmatic.Events/README.md) | Domain events raised on save and dispatched after the commit, a transactional outbox, handler ordering |
| [Pragmatic.Messaging](Pragmatic.Messaging/README.md) | Async messaging: outbox, Channels/RabbitMQ/Kafka/Azure Service Bus transports, saga orchestration |
| [Pragmatic.Jobs](Pragmatic.Jobs/README.md) | Background jobs: recurring/delayed, retry+timeout, lease-based distributed locking |
| [Pragmatic.Migrations](Pragmatic.Migrations/README.md) | Declarative schema diff on Sqlite / PostgreSQL / SQL Server |
| [Pragmatic.Composition](Pragmatic.Composition/README.md) | Source-generated DI, decorators, modules, pipeline steps |
| [Pragmatic.Identity](Pragmatic.Identity/README.md) | ICurrentUser with property composition, local/JWT/OIDC/Keycloak handlers, temporal roles |
| [Pragmatic.Authorization](Pragmatic.Authorization/README.md) | Roles, groups, permission wildcards, resource authorizers, permission cache |
| [Pragmatic.MultiTenancy](Pragmatic.MultiTenancy/README.md) | Tenant resolution strategies, shared-schema and DB-per-tenant |
| [Pragmatic.Logging](Pragmatic.Logging/README.md) | High-performance structured logging with privacy redaction |

### Privacy & Compliance

| Module | Purpose |
|--------|---------|
| [Pragmatic.Privacy](Pragmatic.Privacy/README.md) | Erasure, retention, consent, access and portability — the parts of GDPR that have entities, policies and a lifecycle |
| [Pragmatic.Audit](Pragmatic.Audit/README.md) | An append-only audit trail whose entries can be shown not to have changed, and which survives erasing the people it is about |
| [Pragmatic.Cryptography](Pragmatic.Cryptography/README.md) | Key management and encryption at rest |
| [Pragmatic.Incidents](Pragmatic.Incidents/README.md) | Security incident records and the reporting clocks that run against them — NIS2 windows by default |
| [Pragmatic.Redaction](Pragmatic.Redaction/README.md) | The personal-data shapes worth recognising in free text, in one place |

### Documents & Media

| Module | Purpose |
|--------|---------|
| [Pragmatic.Documents](Pragmatic.Documents/README.md) | PDF / DOCX / XLSX / CSV / Markup / Email templating |
| [Pragmatic.Imaging](Pragmatic.Imaging/README.md) | Rust-native bindings for resize / format / QR code / filters |
| [Pragmatic.Email](Pragmatic.Email/README.md) | Templated email with attachment and inline-image support |
| [Pragmatic.Notifications](Pragmatic.Notifications/README.md) | Multi-channel delivery (email, SMS, push) with audience/priority |

### Medium Blocks

Drop-in packages that ship a complete domain slice:

| Module | Purpose |
|--------|---------|
| [Pragmatic.Identity.Local](Pragmatic.Identity/src/Pragmatic.Identity.Local/) | Self-contained local identity: signup, login, password reset |
| [Pragmatic.Comments](Pragmatic.Comments/README.md) | `[HasComments]` trait → CRUD + moderation per any entity |
| [Pragmatic.Tags](Pragmatic.Tags/README.md) | `[HasTags]` trait → tagging per any entity |
| [Pragmatic.Attachments](Pragmatic.Attachments/README.md) | `[HasAttachments]` trait |
| [Pragmatic.Notes](Pragmatic.Notes/README.md) | Notes entity trait |

### Supporting

| Module | Purpose |
|--------|---------|
| [Pragmatic.Storage](Pragmatic.Storage/README.md) | `IFileStorage` abstraction with LocalDisk / InMemory / Azure / S3 / Google Cloud / FTP / SFTP providers |
| [Pragmatic.FeatureFlags](Pragmatic.FeatureFlags/README.md) | Declarative feature flags with Role/Tenant/Percentage rules |
| [Pragmatic.Discovery](Pragmatic.Discovery/README.md) | Host topology and service discovery |
| [Pragmatic.Client](Pragmatic.Client/README.md) | Compile-time typed HTTP clients generated from manifests |

### Platform (Preview)

| Module | Purpose |
|--------|---------|
| [Pragmatic.Agent](Pragmatic.Agent/README.md) | Local coordination daemon: shared KV, SWIM gossip, IPC |
| [Pragmatic.Gateway](Pragmatic.Gateway/README.md) | YARP-based gateway with Agent-driven routing |

### Tooling

| Module | Purpose |
|--------|---------|
| [Pragmatic.SourceGenerator](Pragmatic.SourceGenerator/README.md) | Unified incremental generator — one analyzer, 31 feature pipelines |
| [Pragmatic.Testing](Pragmatic.Testing/README.md) | Generated contract tests, a typed test client (`Api.*`), HTTP assertions |

</details>

## Documentation

Full documentation site: **[docs.pragmaticdesign.net](https://docs.pragmaticdesign.net)** — and every
module has its own README + `docs/`. Start with what matches your need:

- **Data layer?** [Persistence](Pragmatic.Persistence/README.md) · **Domain actions?** [Actions](Pragmatic.Actions/README.md) · **HTTP endpoints?** [Endpoints](Pragmatic.Endpoints/README.md)
- **DI / startup?** [Composition](Pragmatic.Composition/README.md) · **Auth?** [Authorization](Pragmatic.Authorization/README.md) + [How-to](docs/howto/authentication-authorization.md)
- **Messaging / outbox / saga?** [Messaging](Pragmatic.Messaging/README.md) · **Jobs?** [Jobs](Pragmatic.Jobs/README.md) · **Migrations?** [Migrations](Pragmatic.Migrations/README.md)
- **Errors?** [Result](Pragmatic.Result/README.md) + [Ensure](Pragmatic.Ensure/README.md)

Reading paths: [docs/README.md](docs/README.md) · Roadmap: [docs/ROADMAP.md](docs/ROADMAP.md) ·
End-to-end tour: [Showcase walkthrough](docs/howto/showcase-walkthrough.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines, coding conventions, and how to get started.

## License

Pragmatic.Design is **dual-licensed, per package**:

- **Foundation, capabilities & tooling** (Result, Ensure, Validation, Mapping, the source generator, …) — **[MIT](licenses/LICENSE-MIT.txt)**, free for everyone at any scale.
- **Framework runtime** (Persistence, Actions, Endpoints, Composition, Identity, …) — **[PolyForm Small Business](licenses/LICENSE-PolyForm-Small-Business-1.0.0.txt)**: free for small businesses (under US $1M annual revenue **and** under 100 employees) and for personal, educational, and open-source use; a per-project commercial license applies above that threshold.

Code the generator emits into your project is **yours** — no conversion clause, no lock-in. Full
details, the package map, and commercial licensing: **[docs/LICENSING.md](docs/LICENSING.md)**.

### Why it is split this way

The foundation packages are MIT and that is the end of it: open source, no threshold, no asterisk. The
framework runtime is **source-available, not open source** — you can read it, build it, debug it and
ship it, but above the threshold it is paid. Saying otherwise would be more comfortable and less true.

That split is not a growth tactic. It is the only way I can see to keep a project this size honest
about what it promises. Something that replaces the infrastructure layer of your application has to be
supported for years: security fixes on a timeline, breaking changes carried through with migration
guides, answers on a bad Friday. That is not spare-time work, and a framework that quietly stops
receiving it is worse than one that never existed — you have already built on it. The threshold is what
pays for that continuing, and it falls on the organisations large enough that the framework is saving
them more than it costs.

Two commitments follow from this, and they are the reason to state it out loud rather than leave it in
a licence file.

**The price will never be per developer.** Commercial licenses are per project. Adding people to a team
will never cost more, and nobody will ever have to count seats to stay compliant, or argue about
whether a contractor counts. The size test in PolyForm Small Business decides *whether* you owe
anything at all; it never decides *how much*.

**What you have keeps working.** There is no time bomb and no conversion clause. A version you were
entitled to use stays usable, and the code the generator wrote into your project is yours outright —
it compiles without the generator, and without a license. You are paying for the framework to keep
being maintained, not renting your own source.
