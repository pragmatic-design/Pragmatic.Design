# Casework

Reference example 3 of Pragmatic.Design. A public-service back office that opens a case for an
applicant, asks an external body to verify what the applicant claimed, waits for the answer without
holding anything open, decides, and writes the applicant a decision letter and an e-mail in their own
language. It is **two services** — `Casework.Intake` (the cases) and `Casework.Verify` (the
verifications) — that share **no database and no code but a contracts assembly**, talk over **RabbitMQ**
with an outbox on each side, and serve several organisations at once: most on one shared schema, and the
ones whose row names a database **on a database of their own**, created and migrated while they are
onboarded. The letter and the mail are `.pdxdoc` and `.pdxemail` templates rather than code, and an
organisation replaces any of them — the letter, its letterhead, the mail, its header — one piece at a
time, keeping the application's for the pieces it did not upload.

It is the level the other two examples stop below. [Time off](../time-off/README.md) is one module and
local accounts; [Invoicing](../invoicing/README.md) is two modules in one host, multi-tenant on one
schema. Here the two modules are two processes, the tenant has to survive a broker, and a database can
be created at run time.

## Run it

```bash
docker compose up -d --wait
dotnet run --project src/Casework.Intake.Host      # http://localhost:5210
dotnet run --project src/Casework.Verify.Host      # http://localhost:5211
```

PostgreSQL answers on **5442** (Time off holds 5440 and Invoicing 5441, so the three examples run at
once) and RabbitMQ on 5672, with its management UI on 15672. The database password in the hosts'
`appsettings.json` is that local container's, set in `docker-compose.yml`: not a secret, and not what a
deployment uses. Each service's API reference is at
`/scalar` and its contract at `/openapi/v1.json`. The asynchronous contract is not served over HTTP: it
is an **AsyncAPI 3.0** document the generator writes as a constant into the *contracts* assembly
(`PragmaticAsyncApi.Json`), so it comes from the one assembly a consumer references. The channel's
address is the topic the broker carries the event on — `intake.events` — and the document names the rule
that produced it (`x-pragmatic-address-rule`), because the generator writes it at compile time while the
router is a runtime service: `tests/…/TheAsyncApiPublishesTheContract.cs`. The migration runs at startup
and creates each schema from the entities, so `docker compose down -v` and up again is the way back
from a schema change the runner refuses.

**Every route needs a token this service signed, and a tenant in it.** Casework holds no accounts: the
`sub` is the user, each role is its **own `role` claim** — `caseworker` and `service-operator` on
Intake, `verification-service` on Verify, repeated once per role and not a JSON array — and `tenant_id`
is the organisation's key. Those are the names the pipeline reads, and the suite mints its tokens
through the host's own `JwtTokenGenerator` rather than spelling them a second time
(`Infrastructure/TestTokens.cs`). The tenant comes from the claim and from nowhere else — a header or a
route segment is input the caller controls. An organisation
has to exist in the register first (`POST api/organisations`, which is the `service-operator`'s route
and belongs to no tenant), because `RequireKnownTenant` answers **404** to an id that names nobody.

## Test it

```bash
node scripts/check.mjs --tier docker --only Casework.IntegrationTests   # from the repository root
```

**113 passed and none skipped, in 2m 53s** inside a `--tier all` run; the command above pays its own
clean build on top. The suite
waits for delivery in several places, so the outbox polling interval — two seconds on this host against
the framework's five (`Casework.Intake.Host/Program.cs`) — is a test-duration decision as much as a
tuning one. **All but thirteen of them boot both hosts, a real RabbitMQ and N PostgreSQL databases** and
talk HTTP to them: no in-memory transport, no test double for the broker, no test-only authentication
anywhere — the suite signs its own tokens with the key the host is given. The other thirteen need no
host at all, and that is the point of them: three read the generated AsyncAPI document, seven inspect
what the two assemblies reference (`TheAsyncApiPublishesTheContract`, `TheContractIsTheOnlyThingShared`),
and three read the contract generator's own coverage report (`TheGeneratedContractsCoverEveryOperation`).

It is the **third most expensive suite of the tier**, after TimeOff's 4m 29s and Showcase's 3m 25s,
and the tier runs one suite at a time on purpose — so this is added time and not added parallelism.
The whole tier, measured inside `node scripts/check.mjs --tier all` (a late run, where the clean build
was already paid for): **20 suites, 3,003 passed and none skipped, in 16m 54s**. Casework was
**2m 53s** of it, about 17%. A timing from a late run is not comparable with one from the morning — the machine drifts within a
session (`docs/TESTING.md` has the measurement) — so a delta is read between two consecutive runs and
not against this paragraph.

What the 2m 53s buys is the only place in this repository where a message crosses a process boundary
for real. What would make it cheaper, said as numbers rather than as an intention:

- **Two pairs of containers, not one.** The hand-written tests share one PostgreSQL and one RabbitMQ
  (`Infrastructure/IntegrationCollection.cs`), and the generated contract tests run in the framework's
  own collection with a **second** pair of their own (`Infrastructure/ContractFixture.cs`), because a
  collection fixture cannot be shared across collections. Two brokers to start is the single largest
  fixed cost here.
- **Every dedicated database is a full migration.** Four classes onboard organisations with databases
  of their own — `ADatabasePerOrganisation`, `MigratingEveryDatabaseAtOnce`,
  `OnboardingCrossesBothServices`, `TheTenantTravelsOnTheMessage` — two to four databases each, because
  each service provisions its own, and each one runs the whole schema.
- **The tier's own width.** `--docker-jobs 2` takes the whole tier down by about a quarter and the
  default stays 1 on purpose; that is a tier-wide decision, documented in `docs/TESTING.md`, not this
  suite's to make.

**80 of the 113 tests are hand-written and 33 are generated** by
`Pragmatic.Testing.SourceGenerator` from the endpoints — 26 authorization contracts on Intake, 3 CRUD,
2 authorization contracts on Verify and 2 state transitions, counted as the
`FactAttribute` occurrences in `obj/Debug/net10.0/generated/Pragmatic.Testing.SourceGenerator/` right
after `generated-code-stats.mjs` rebuilt the project, whose report finds the same seven generated
files — an incremental build leaves files from the previous compilation behind, and counting those
counts a mixture. The 80 are 79 methods, one of them a theory over both contracts assemblies.
All of the generated pass. A `Verification` has no create endpoint (the row is written by a message
handler), so its transition tests bring it into its starting state through the arrange the fixture
registers (`PragmaticContractHost.ArrangeFor` in `Infrastructure/ContractFixture.cs`). Among the generated ones is
`CreateOpenCaseMutation_IsNotVisibleToAnotherTenant`, the isolation contract — one test, on Intake's
one create mutation. Every operation declares the permission it needs, which is what gives the
generator a caller to refuse and a contract to emit. What runs against both services is the fixture: it registers **a client per
boundary** — `PragmaticContractHost.UseClientFor("Intake", …)`, `("Verify", …)` — and each generated
class, which knows which boundary it is, goes to the host that owns it
(`Infrastructure/ContractFixture.cs`). ⚠️ Not one client routed by **route prefix**: a route added to
Verify under a prefix the table did not name would go to Intake and answer 404, which reads as a
contract failure about the wrong service.

## What it demonstrates, and where to look

| | Where |
|---|---|
| **Two services, one contract** — Verify never sees Intake's entities, only a record in a contracts assembly, and a test asserts that is all that is shared | `src/Casework.Intake.Contracts/`, `tests/…/TheContractIsTheOnlyThingShared.cs` |
| **The outbox** — the event is a row of the same transaction as the case, and is delivered after the commit | `tests/…/TheOutboxHoldsWhatTheTransactionWrote.cs`, `…/TheOutboxDeliversWhatItHeld.cs` |
| **At-least-once, and what makes it harmless** — the same answer twice changes nothing, because the aggregate records which event decided it | `Casework.Intake/Cases/Case.cs` (`AnsweredByEvent`), `tests/…/AnOutcomeThatComesBackTwice.cs` |
| **A message that cannot be handled** — it ends in the broker's dead-letter queue, and the test reads it there | `tests/…/AMessageThatCannotBeHandled.cs` |
| **A process that waits without holding anything** — nothing in Intake blocks on the answer. A saga follows the conversation: it starts on the request, waits, and decides the case when the answer arrives, and its instance and steps are rows a test reads back with SQL | `Casework.Intake/Infrastructure/Sagas/CaseProcessSaga.cs`, `tests/…/NothingInIntakeWaitsForTheAnswer.cs`, `…/TheProcessThatCarriesACase.cs` |
| **Giving up undoes what was asked** — an inconclusive answer makes the saga refuse, and the compensation withdraws the verification the step that already ran had asked for | `CaseProcessSaga.cs` (`[CompensateWith]`), `tests/…/TheProcessGivesUpAndUndoesWhatItAsked.cs` |
| **An answer nobody can record is not lost** — an outcome for a case Intake does not hold ends in the dead-letter queue instead of being acknowledged | `Casework.Intake/Infrastructure/MessageHandlers/RecordTheVerificationOutcome.cs`, `tests/…/AnOutcomeForACaseNobodyKnows.cs` |
| **The asynchronous contract, generated** — the event, its payload and its public marker, in an AsyncAPI document that is a constant of the contracts assembly; the channel's address is the topic the broker carries it on | `tests/…/TheAsyncApiPublishesTheContract.cs` |
| **The key a message is partitioned by** — `[PartitionKey]` on the request's case id, stamped on every message about that case and read back from the wire. The ordering it enables needs a partitioned transport, and RabbitMQ is not one: the header is demonstrated, the ordering is not | `Casework.Intake.Contracts/VerificationRequested.cs`, `tests/…/TheKeyAMessageIsPartitionedBy.cs` |
| **The tenant survives the broker** — it travels in `X-Pragmatic-TenantId` and is re-entered on the other side, so a handler writes into the right database and a message with no tenant is refused | `tests/…/TheTenantTravelsOnTheMessage.cs`, `Casework.Verify/Infrastructure/MessageHandlers/RecordTheVerificationRequest.cs` |
| **A database per organisation, beside a shared schema** — one deployment, both models, chosen by whether the row names a connection string | `Casework.Intake.Host/Program.cs` (`UseDbPerTenant`), `tests/…/ADatabasePerOrganisation.cs` |
| **Onboarding is itself distributed** — the organisation is registered `Provisioning`, both services create and migrate their database, and it becomes `Active` when the second half answers | `Casework.Intake/Organisations/Actions/OnboardAnOrganisationAction.cs`, `…/MessageHandlers/CompleteTheOnboarding.cs`, `tests/…/OnboardingCrossesBothServices.cs`, `…/OnboardingWhenTheSecondHalfFails.cs` |
| **Migrating N databases in one run** — and a report that names which ones moved, and which were not visited | `Casework.Intake.Host/TheMigrationOfEveryDatabase.cs`, `tests/…/MigratingEveryDatabaseAtOnce.cs` |
| **The letter is the organisation's own** — a `.pdxdoc` it uploaded, with its own header partial, rendered on download to PDF or, from the same document model, to a Word file a caseworker can amend | `Casework.Intake/Letters/`, `…/Infrastructure/Letters/TheDecisionLetter.cs`, `…/Cases/Endpoints/DownloadTheDecisionLetterAsWordEndpoint.cs`, `tests/…/TheLetterIsTheOrganisationsOwn.cs`, `…/TheLetterAsAWordFile.cs` |
| **In the applicant's language** — frozen when the case is opened, and the culture scope is entered in one place | `Casework.Intake/Infrastructure/Letters/TheDecisionLetter.cs` (`I18NContext.WithCultureAsync`), `tests/…/TheLetterIsInTheApplicantsLanguage.cs` |
| **The mail, from the same shape** — a `.pdxemail` with the organisation's partial, sent from the organisation's own address or not at all | `Casework.Intake/Infrastructure/Mail/TheDecisionMail.cs`, `tests/…/TheMailComesFromTheOrganisation.cs` |
| **A deadline nobody answers** — an hourly job expires the verifications that ran out of time, over the tenants | `Casework.Intake/Infrastructure/Jobs/`, `tests/…/ADeadlineThatNobodyAnswers.cs` |
| **Documents kept per organisation** — an upload stored under the tenant's own container, downloaded back through a permission | `Casework.Intake/Cases/CaseDocumentStorageKey.cs`, `tests/…/TheCasesDocuments.cs` |
| **The whole surface, asserted** — the eleven routes Intake maps, read from its own generated document, and the control that the `status` in an update body moves nothing | `tests/…/TheCasesSurface.cs` |

## Why it is built this way

**The tenant travels in the message, because a handler has no request.** Multi-tenancy resolves the
tenant from the caller's token, and a message has no caller: the publisher stamps the current tenant on
the message and the subscriber re-enters that scope before the handler runs. Without it a handler runs
with no tenant and either writes into the shared schema or refuses — and both look like the handler's
bug.

**Each host names the contracts assembly's generated registry, in one line.** A message is resolved by
its fully qualified type name, and the generator emits an `IMessageTypeRegistry` for every assembly that
declares domain events — so the registry for `VerificationRequested` is generated where that record
lives, in the contracts project. The host calls it explicitly because the composition discovers
*modules*, and a contracts assembly deliberately is not one. ⚠️ A registry derived from an assembly's
`[MessageHandler]`s alone would leave a service that only publishes with none, and it would
dead-letter every row of its own outbox as an "unknown message type". A hand-written switch standing
in for the registry is no fix: a forgotten line there is a lost message rather than a compile error.

**Onboarding is a two-phase state and not a boolean.** An organisation is registered `Provisioning`,
each service creates and migrates its own database, and only when the second service says it is ready
does the row become `Active` — with `EnforceTenantState` refusing every request in between with 403. A
boolean `IsActive` cannot express "registered and not yet usable", so the first request of a new
organisation would race the creation of its database and fail with `3D000: database does not exist`.

**A database is created and migrated explicitly, over the bus.** The framework has no "provision on
first access", and no option promises one. `UseAutoProvision<T>()`
chooses *which* provisioner issues `CREATE DATABASE`; calling it is the application's, and it creates an
**empty** database. So the provisioning that actually happens is an action that creates the database,
puts this service's schema in it, and publishes `TenantOnboarded` for the other service to do the same.
The two steps stay two because the schema to migrate to (`IntakeDatabaseSchema.Current`) is generated
into the host and no framework assembly can name it.

**The letter is the organisation's template and not the application's.** Two organisations deciding the
same case send different-looking letters, in the applicant's language, and neither is a code change:
the `.pdxdoc` is a row they uploaded, its `<import src>` names a header partial that is another row of
theirs, and every sentence in it is a translation key. Two decisions in that one method, and they are
not the same decision. The culture scope is opened **once**, around the whole render, so that "the
letter is in the applicant's language" is a property of writing a letter instead of a line somebody has
to remember. And the language an applicant who named none falls back to is the one the host was
**configured** with, deliberately not `I18NContext.Current`: outside a scope that property reads the
thread's culture, and `WithCultureAsync` documents that it does not restore it after an await — so a
letter with no language would come out in whatever culture another render left on that thread. A job
is in that situation as much as a test.

**The mail is sent from the organisation's own address, and there is no default sender.** A default
would mail every organisation's applicants from one place, and a reply would reach the wrong people
about somebody else's case. An organisation with no address sends nothing, and the handler says so
rather than falling back.

## The numbers, measured

Measured with `node scripts/generated-code-stats.mjs casework`, which rebuilds each
project with its generated files written to a folder emptied first — `obj/…/generated/` is never
emptied, so counting it counts types renamed days ago. A line counts when it is neither blank nor a
comment; the 2,707 lines of generated-file banner are left out of every count. The full per-file report
is `artifacts/generated-code-stats/report.json`.

| | Files | Lines |
|---|---:|---:|
| Written — `src/Casework.Intake` | 55 | 1,366 |
| Written — `src/Casework.Intake.Contracts` | 2 | 16 |
| Written — `src/Casework.Intake.Host` | 7 | 287 |
| Written — `src/Casework.Verify` | 14 | 226 |
| Written — `src/Casework.Verify.Contracts` | 3 | 21 |
| Written — `src/Casework.Verify.Host` | 7 | 239 |
| **Generated by Pragmatic — `Casework.Intake`** | 185 | 10,125 |
| **Generated by Pragmatic — `Casework.Intake.Contracts`** | 8 | 131 |
| **Generated by Pragmatic — `Casework.Intake.Host`** | 23 | 2,729 |
| **Generated by Pragmatic — `Casework.Verify`** | 70 | 3,084 |
| **Generated by Pragmatic — `Casework.Verify.Contracts`** | 7 | 120 |
| **Generated by Pragmatic — `Casework.Verify.Host`** | 20 | 1,287 |
| Written — `tests/Casework.IntegrationTests` | 38 | 3,222 |
| Generated by Pragmatic in the test project | 7 | 585 |

**2,155 lines of application** in 88 files, against **17,476 generated by Pragmatic** in 313 — **8.1
times as much**, and the two hosts' fourteen files are 526 lines of composition for two services, a
broker, two schemas and a database per tenant. Five more files and 1,197 lines come from generators that
are not ours (`LoggerMessage`, ASP.NET's OpenAPI XML-comment helper, `PublicProgram`) and are counted
apart. Part of the Pragmatic total is data rather than logic: the largest single artefact is the
**repositories** (7 files, 2,551 lines, 14.6% of what the generator wrote), then the two hosts' OpenAPI
documents (2 files, 1,507 lines together) and the four manifest files (1,002).

Behind those lines: **2 modules** in **2 services** (plus a contracts assembly each, and two hosts),
**7 entities** — `Case`, `CaseDocument`, `LetterTemplate`, `CorrespondenceSettings` and `Organisation`
in Intake, `Verification` and its own `Organisation` in Verify — and **16 operations**: 4 mutations,
8 actions, 1 query and 3 endpoint classes of their own (the document download and the letter, as PDF and
as Word). Twelve of them are published as **12 routes**, 11 on Intake and 1 on Verify; the other four
are called by a message handler and never reachable over HTTP. `TheCasesSurface.NoRouteWritesAStatus`
asserts that Intake's eleven are exactly the operations its generated document publishes, one for one.

Beside the operations: **7 message handlers**, **1 saga**, **1 recurring job**, **3 services**,
**3 roles**, **4 templates** (two `.pdxdoc`, two `.pdxemail`) and **20 translation keys** in each of
`en-US` and `it-IT`.

To read the generated code, build once and open
`src/Casework.Intake/obj/Debug/net10.0/generated/` (`EmitCompilerGeneratedFiles` is on for this
example). ⚠️ That folder is never emptied, so a file a generator no longer writes stays there and a name
read from it is not evidence that the generator still writes it — which is why the table above comes
from `generated-code-stats.mjs` and not from a count of that folder. Good places to start:

- `Casework.Intake.Cases.Actions.DecideCaseAction.Invoker.g.cs` — the pipeline an action runs in
- `Casework.Intake.Infrastructure.Sagas.CaseProcessSaga.Orchestrator.g.cs` — the saga, as declared
- `_Infra.Messaging.DispatchTable.g.cs` and `_Infra.Messaging.TypeRegistry.g.cs` — how a message that
  arrives finds its handler, decided at compile time rather than looked up

## What it is not

- **No UI**, no client, no deployment: the example stops at two `dotnet run`.
- **No identity provider.** Casework signs its own tokens and stores no account — that is Invoicing's
  chapter (OIDC) and Time off's (local accounts).
- **No exactly-once.** The transport is at-least-once, the idempotency store is **in memory**, and what
  makes a repeated answer harmless is the aggregate, not the infrastructure.
- **No privacy chapter** — classification, export and erasure are Time off's.
- **No money**, no PDF invoice, no job persistence beyond the deadline sweep's own tables.
- **No second broker and no transport failover**: RabbitMQ or nothing.

**What is skipped.** Nothing. A `Verification` has no create endpoint, so the generated transition
test brings it into its initial state through an arrange the application registers.

**What keeps the generated contracts honest.** The generator emits a **coverage report** beside the
tests — every published operation, what was emitted for it, and one sentence per contract it declined —
and `TheGeneratedContractsCoverEveryOperation` pins it, so an operation that stops being contracted fails
a test instead of joining a silence.

## Where the capabilities it uses are documented

[Actions](../../Pragmatic.Actions/README.md) ·
[Endpoints](../../Pragmatic.Endpoints/README.md) ·
[Persistence](../../Pragmatic.Persistence/README.md) ·
[Mapping](../../Pragmatic.Mapping/README.md) ·
[Validation](../../Pragmatic.Validation/README.md) ·
[Messaging](../../Pragmatic.Messaging/README.md) ·
[MultiTenancy](../../Pragmatic.MultiTenancy/README.md) ·
[Identity](../../Pragmatic.Identity/README.md) ·
[Authorization](../../Pragmatic.Authorization/README.md) ·
[Internationalization](../../Pragmatic.Internationalization/README.md) ·
[Documents](../../Pragmatic.Documents/README.md) ·
[Storage](../../Pragmatic.Storage/README.md) ·
[Jobs](../../Pragmatic.Jobs/README.md) ·
[Email](../../Pragmatic.Email/README.md) ·
[Testing](../../Pragmatic.Testing/README.md) ·
[Migrations](../../Pragmatic.Migrations/README.md)
