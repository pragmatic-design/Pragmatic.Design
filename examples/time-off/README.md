# Time off

Reference example 1 of Pragmatic.Design. Employees ask for leave, managers decide for their team, and
HR administers people, allowances, kinds of absence and company holidays. The employees' personal data
is classified, exportable and erasable, and the audit trail of decisions survives the erasure.

It is a single-module monolith on PostgreSQL. Users sign in with local accounts and a JWT, and the
application speaks English and Italian.

## Run it

```bash
docker compose up -d
dotnet run --project src/TimeOff.Host
```

The API reference opens at <http://localhost:5180/scalar> and the OpenAPI document is at
<http://localhost:5180/openapi/v1.json>. In Development the first HR administrator comes from
`appsettings.Development.json`: `hr@time-off.example`, with the password written there. Sign in with
`POST /identity/local/sign-in` and send the token back as `Authorization: Bearer …`.

The migration runs at startup and creates the schema from the entities; there is no migration history
to apply. A database left over from an earlier version of the example can hold a change the migration
refuses to apply without `Force` (a breaking one). The data is disposable, so start from an empty
database instead: `docker compose down -v`, then `up -d` again. The database password in
`appsettings.json` is that local container's, set in `docker-compose.yml`: not a secret, and not what a
deployment uses.

## Test it

```bash
node scripts/check.mjs --tier docker --only TimeOff.IntegrationTests   # from the repository root
```

68 tests, around two minutes (1m 55s–2m 10s over several runs). Each test boots the real
host and talks HTTP to it: PostgreSQL in a container, signing in as a client does, no test-only
authentication. The suite is part of the gate's docker tier, and CI runs that tier.

## What it does, and where

Each story is a folder of the module and a test class.

| Story | What it shows | Code (`src/TimeOff.Leave/`) | Test |
|---|---|---|---|
| Start the application with one command | host composition, the database marker, migrations with a committed schema snapshot | `../TimeOff.Host/TimeOffHostModule.cs`, `../TimeOff.Host/schema/` | `TheApplicationStarts` |
| HR registers employees | entities and relations, a generated `EMP-00001` number, unique email → 409, `[Auditable]` | `Employees/` | `RegisteringEmployees` |
| Kinds of absence in two languages | `LocalizedString` content, a rule on the entity | `AbsenceKinds/`, `Allowances/` | `DefiningKindsOfAbsence`, `GrantingAllowances` |
| Asking for time off | working days on the Italian calendar, overlap and allowance as typed errors | `LeaveRequests/Actions/SubmitLeaveRequestAction.cs`, `Infrastructure/Calendar/` | `AskingForTimeOff` |
| Deciding and withdrawing | a state machine, row visibility by team, a domain event recorded in the audit trail | `LeaveRequests/`, `Infrastructure/EventHandlers/RecordTheDecision.cs` | `DecidingLeaveRequests` |
| The balance left | `[Projectable]` members summed by the database, not in memory | `Allowances/Allowance.cs`, `Allowances/Queries/` | `SeeingTheBalance` |
| The team calendar | a declarative query: filters, sort, paging, bounded by the same visibility | `LeaveRequests/Queries/TeamCalendarQuery.cs` | `SeeingTheTeamCalendar` |
| The user's language | translations checked at compile time, the profile's language, localized errors | `translations/`, `Infrastructure/Internationalization/` | `SpeakingTheUsersLanguage` |
| Who may do what | local accounts with JWT, roles mapped to permissions, every endpoint declaring one | `Infrastructure/Authorization/`, `Infrastructure/Identity/EmployeeClaims.cs` | `WhoMayDoWhat`, `SigningIn` |
| Personal data | classification, export, erasure, the processing register, a verifiable audit trail | `Employees/Employee.cs`, `Employees/Actions/`, `Compliance/`, `Infrastructure/Privacy/` | `HandlingPersonalData`, `ErasingAnEmployee` |

## How it is built, and why

- **One module, one boundary** (`TimeOff.Leave`). The folders are groups of operations named after
  their routes; each entity sits with the operations that write it.
- **The employee is the user.** `Employee` owns its local account (`[PragmaticUser]`). HR registers
  someone, the account opens with a password nobody knows, and the invitation is a password reset.
- **What the application records about who acted is a pseudonym.** The token's subject is the
  employee's reference in the subject registry, not their email. The rows they write, their requests'
  access scopes and the audit trail therefore hold a reference that erasure can make meaningless. The
  audit trail is append-only, so an email written there could never be erased.
- **Visibility is data, not a filter.** A request carries its access scopes (the requester, the
  team's manager role). The same row filter gives a manager their team, an employee their own requests
  and HR everything, in every query.
- **Rules live where they cannot be skipped.** The counting rule of a kind of absence is on the
  entity, and state transitions are declared on the status. What is left of an allowance is a
  `[Projectable]` expression that both the balance and the submission read.
- **A rule about which rows count has a name, and is written once.** "Approved", "still holding its
  days", "managed by" are specifications, in the other half of each entity's generated
  `{Entity}Specifications` (`LeaveRequestSpecifications`, `TeamSpecifications`, …), and every read names
  them: the queries, the actions (`repo.FindManagedByAsync(id)`), and the balances and filters the
  entities compute (`Requests.Where(LeaveRequestSpecifications.Approved).Sum(...)`), which the
  database receives as SQL.
- **An entity named by the request is loaded by the invoker.** `[LoadEntity<AbsenceKind>]` on the
  submission, `[LoadEntity<Employee>]` on the erasure — past the soft-delete filter the erasure lifts:
  404 before `Execute`, never a lookup and a null check in it.
- **What a request must say is checked before it runs.** The period in order and in one year, the
  hours within a working day, are attributes on `SubmitLeaveRequestAction`; what depends on the kind of
  absence is its async validator. `Execute` answers only what the domain can: the working days, an
  overlap, the allowance.
- **Messages are symbols.** `T.Validation.LeaveRequest.EndsBeforeItStarts` is generated from
  `translations/*.json`, and a key missing in one language fails the build. An attribute names the
  same key through its constant, `TKeys.Validation.LeaveRequest.EndsBeforeItStarts`.
- **Erasure is declared, then run.** Each personal field says how it is erased: pseudonymised, emptied
  or cleared. The generator writes the export sources and the erasure steps from that. The one step
  written by hand closes the account (`Infrastructure/Privacy/CloseTheAccount.cs`).

## What the generator wrote

Measured on a fresh build of each project (`--no-incremental`, with the generated files
written to an empty folder, because `obj/` keeps files left over from earlier builds). A line is a
non-blank line that is not a comment.

| | Files | Lines |
|---|---:|---:|
| Written: the module (`src/TimeOff.Leave`) | 93 | 1,926 |
| Written: the host (`src/TimeOff.Host`) | 9 | 209 |
| **Generated by Pragmatic, for the module** | 301 | 19,028 |
| **Generated by Pragmatic, for the host** | 27 | 5,795 |
| Written: the tests (`tests/TimeOff.IntegrationTests`) | 24 | 1,411 |

For the 6 entities and 33 operations (13 mutations, 9 queries, 11 actions) behind 34 endpoints:

- **for the operations**:
  - an invoker for each, which runs it through the framework's pipeline and commits its unit of work;
  - validators, and a request body for each one that takes one;
  - an endpoint for each one that is exposed;
- **for each entity**:
  - the repository,
  - setters,
  - specifications,
  - projections,
  - the EF configuration;
- **for the boundary** (`_Boundary.LeaveBoundary.*`): the typed interface every operation is called
  through, in process or remotely;
- **for privacy**:
  - the export sources,
  - the erasure plans and steps,
  - the processing register;
- **for the host**:
  - the DbContexts,
  - the service registrations,
  - the migration schema.

Some of it is data rather than logic: the module's manifest (2,585 non-blank lines) and the host's
OpenAPI document (4,038) are among the generated lines.

To read it, build once and open `src/TimeOff.Leave/obj/Debug/net10.0/generated/` and the same folder
under the host. `EmitCompilerGeneratedFiles` is on for this example so these files exist to be read.
Good places to start:

- `TimeOff.Leave.LeaveRequests.Actions.SubmitLeaveRequestAction.Invoker.g.cs`
- `TimeOff.Leave.Entities.Allowance.Projectable.g.cs`
- `TimeOff.Leave.Entities.EmployeeErasurePlan.PrivacyErase.g.cs`

## Known limits

- Framework errors without a translation of the application's own (not found, the state machine's
  conflict) keep their English wording. A missing permission has one (`error.forbidden.*`), with the
  permission in its place, whichever layer refuses.
- A request that names a language (`Accept-Language`) is answered in it even when the profile says
  otherwise: that is the framework's precedence.
- Signing in is the identity package's `SignInUser`, exposed at `identity/local/sign-in`: it checks the
  credentials and the JWT issuer `UseJwtAuthentication` registers signs the token. What the token says
  about the employee — their reference as the subject, their role, the teams they manage — is
  `Infrastructure/Identity/EmployeeClaims.cs`, the one piece Time off writes.
