---
name: pragmatic-use-audit
description: Use when the app must prove who changed or accessed something, answer an auditor, verify the trail or choose [Auditable] vs [Audited] — Pragmatic.Audit, sealing, retention, NIS2 incidents. Erasure is pragmatic-use-privacy.
---

# Pragmatic Use Audit

**Covers:** Record who did what, on whose authority, with Pragmatic.Audit — an append-only, tamper-evident trail ([Audited], security and message events), verifying, sealing and retention, NIS2 incidents (Pragmatic.Incidents).

Two things are called "audit" and they are different:

| | `[Auditable]` | `[Audited]` |
|---|---|---|
| What | `CreatedAt/By`, `UpdatedAt/By` **on the row** | an entry per change **on the trail** |
| Answers | who last touched this row | everything that happened to it, in order, provably unaltered |
| Cost | four columns | a write per change, in the same transaction |

Most entities want `[Auditable]`. Put `[Audited]` on what somebody will ask about later: money,
permissions, personal data, decisions.

## The trail

- **Append-only and tamper-evident.** Entries are grouped into time segments; a sealed segment gets a
  Merkle root chained to the previous one. Verification reveals an altered row; it does not prevent one —
  that needs database permissions or WORM storage.
- **No personal values, ever.** An entry names the actor and the subject by **pseudonym** (`ActorRef`,
  `SubjectRef`, from the subject registry of `pragmatic-use-privacy`) and proves a change with a
  `ValueHash`, not the old value. There is no payload field. That is what lets the trail stay complete
  after the person it is about has been erased.

## Turning it on

In a Pragmatic host with an `[Audited]` entity and `Pragmatic.Audit.EFCore` referenced there is nothing
to write: the generated host registers `AuditDbContext` and `AddAuditTrail()` on that entity's database,
and the migration creates the tables. Elsewhere:

```csharp
services.AddDbContext<AuditDbContext>(o => o.UseNpgsql(connectionString));
services.AddAuditTrail();                   // store + the sealing worker
```

⚠️ `[Audited]` entities in more than one database: the generated host cannot choose where the trail
lives and says so — register `AuditDbContext` yourself.

What else writes to it, once it exists:

| Source | How |
|---|---|
| `[Audited]` entities | Created/Updated/Deleted, with the user and correlation id, in the save's transaction |
| Sign-in failures, lockouts | `AddIdentitySecurityAuditing<TLocator>()` — `pragmatic-use-identity` |
| Message handling outcomes | `msg.EnableAuditing()` in `UseMessaging` (`Pragmatic.Messaging.Auditing`) |
| Reads of personal data | `[RecordAccess]` on the operation — `pragmatic-use-privacy` |
| Configuration changes | the configuration store, transactionally |

## Writing an entry yourself

```csharp
await trail.RecordAsync(new AuditEntry
{
    SegmentId = string.Empty,                        // assigned by the store
    OccurredAt = clock.UtcNow,
    Category = AuditCategory.Security,               // Security, Data, Configuration, Message, Privacy
    Operation = "Billing.RefundApproved",            // a constant — never assembled at runtime
    ActorRef = actorRef,
    OnBehalfOfRef = onBehalfOfRef,                   // when a delegation was in force
    SubjectRef = subjectRef,                         // a pseudonym, never an email or a name
    TargetType = nameof(Invoice), TargetId = invoice.Id.ToString(),
    Outcome = AuditOutcome.Success,                  // Success, Denied, Failed
}, ct);
```

Inject `IAuditTrail` to write. ⚠️ `Detail` is free text and the one place personal data could leak back
in: keep it to codes and references (a detail redactor scrubs known patterns, but do not rely on it).

**Same fate as the change.** When the change and its entry must commit together, write through
`ITransactionalAuditTrail` inside the transaction — the trail's tables must be in the same database, and
if it cannot enlist it **throws** rather than write outside the transaction. A producer on raw ADO.NET
uses `AdoNetAuditTrail` (`Pragmatic.Audit.AdoNet`) with a dialect.

## Reading and verifying

`IAuditTrailReader` is a separate contract — nearly everything writes, very little should read — so a
policy can gate it on its own:

```csharp
var page = await reader.QueryAsync(new AuditQuery
{
    SubjectRef = subjectRef,                         // there is no filter by identity, only by pseudonym
    Category = AuditCategory.Data,
    From = from, Until = until, Limit = 100,
}, ct);

var report = await reader.VerifyAsync(from, until, ct);
if (!report.IsIntact)
    TrailBroken(logger, report.BrokenSegmentIds);    // naming the segment is the point
```

⚠️ **`VerifyAsync` checks sealed segments only.** The sealing worker (registered by `AddAuditTrail()`)
seals a segment once it is closed plus a grace period — sealing the open window would exclude writes in
flight and raise false alarms. A trail nobody verifies proves nothing: schedule `VerifyAsync`
(`pragmatic-use-jobs`) and alert on a broken report.

## Retention

Whole sealed segments only, recorded as a `PrunedRange` that verification steps over — removing single
entries would change a sealed root and look exactly like tampering. The window is a legal decision, so
it is an input, not configuration:

```csharp
[UsePackage<AuditManagementPackage, AdminBoundary>]
[ExposeEndpoint<PruneAuditTrail>(HttpVerb.Post, "audit/prune")]   // permission audit.trail.prune
public sealed class AdminModule;
```

Call `PruneAuditTrail { RetentionDays = … }` from a recurring job, an admin screen, or by hand — the
framework does not schedule it. Zero or negative days is refused.

## From the trail to an incident

`Pragmatic.Incidents` keeps the record of a security incident and the reporting clocks that run against
it; `Pragmatic.Incidents.Audit` raises incidents from patterns in the trail.

```csharp
// AuditPatternDetector(IAuditTrailReader, TimeProvider) — from a recurring job
var raised = await detector.ScanAsync([
    new DetectionRule { Operation = "Security.LoginFailed", Window = TimeSpan.FromMinutes(15),
                        Threshold = 10, PerSubject = true,  Summary = "Repeated failed sign-ins" },
    new DetectionRule { Operation = "Security.LoginFailed", Window = TimeSpan.FromMinutes(15),
                        Threshold = 50, PerSubject = false, Summary = "Sign-in failures across accounts" },
]);
```

- **Configure both forms.** Per-subject finds many attempts on one account; global finds a few on many.
  Attempts on accounts that do not exist carry no pseudonym, so only the global rule sees spraying.
- The detector remembers nothing: overlapping scans raise the same incident twice. The incident id is
  derived from what triggered it — deduplicate on it where you persist incidents.
- **Deadlines run from detection**, not from when the incident happened or was written down.
  `IncidentDeadlines.Nis2` is 24 h / 72 h / one month; the regime is a property you set per sector and
  member state. `incident.Assess(notifiable, note, now)` requires the note — "not notifiable" without a
  reason reads as nobody having looked. `IsOverdue(now)` and `NextObligation` drive the escalation, which
  goes through whatever the deployment already uses (`pragmatic-use-notifications`).
- ⚠️ The module **does not decide** whether an incident is notifiable, and does not persist incidents:
  both are yours.

## Testing

Assert on the entry, not on the log: after the operation, `reader.QueryAsync` for its `TargetType` /
`TargetId` and check `Operation`, `Outcome` and `ActorRef`. For anything security-relevant, test the
denied path too — a `Denied` entry is as much a requirement as the `Success` one.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing and Time off example
applications — code that compiles and that `Invoicing.IntegrationTests` and `TimeOff.IntegrationTests`
exercise — and kept identical to it by the gate: an entity with both `[Auditable]` and `[Audited]`, the
operation names kept as constants, a handler writing what an invoice's move meant, the verification of
the sealed segments, and the incident scan over failed sign-ins.

Retention (`PruneAuditTrail`) and `ITransactionalAuditTrail` are used by no tested application yet, so
there is no example of them here: the sections above are the reference.
