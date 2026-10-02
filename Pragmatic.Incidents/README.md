# Pragmatic.Incidents

Security incident records and the reporting clocks that run against them — NIS2 windows by default.

## The line this module does not cross

It notices, starts the clocks, and makes the remaining time visible. **It does not decide whether an
incident is notifiable.** That is a judgement about impact, and automating it produces both kinds of
error: a stream of reports nobody reads, and silence about the one that mattered.

What it does do is the arithmetic that is easy to get wrong by hand — against a deadline that started at
a moment nobody wrote down.

## Quick start

```csharp
var incident = new SecurityIncident
{
    IncidentId = "INC-2026-0031",
    DetectedAt = timeProvider.GetUtcNow(),
    Summary = "Unexpected bulk export of customer records",
};

var now = timeProvider.GetUtcNow();
incident.Assess(notifiable: true, note: "Personal data of ~4k customers left the system.", now);

if (incident.IsOverdue(now))
    escalate(incident.NextObligation);
```

`Assess` requires a note. "Not notifiable" without a reason is indistinguishable from nobody having
looked — and that is the version an inspection will assume.

## Deadlines run from detection

Not from when the incident happened, which is usually unknowable, and not from when someone got round to
recording it — otherwise a delay in recording silently buys more time. `IncidentDeadlines.Nis2` is
24 hours / 72 hours / one month; every window is configurable, because the applicable regime depends on
the sector and the member state.

## Detecting from the audit trail

`Pragmatic.Incidents.Audit` raises incidents from patterns in `Pragmatic.Audit` — repeated failed
sign-ins, lockouts, permission denials.

```csharp
var incidents = await detector.ScanAsync([
    new DetectionRule { Operation = "Security.LoginFailed", Window = TimeSpan.FromMinutes(15),
                        Threshold = 10, PerSubject = true,  Summary = "Repeated failed sign-ins" },
    new DetectionRule { Operation = "Security.LoginFailed", Window = TimeSpan.FromMinutes(15),
                        Threshold = 50, PerSubject = false, Summary = "Sign-in failures across accounts" },
]);
```

**Configure both forms.** Per-subject finds many attempts against one account; global finds a few against
many. Per-subject counting cannot see attempts against accounts that do not exist — those entries carry
no subject pseudonym by design — and enumerating non-existent accounts is exactly a spraying pattern.

The detector is a pure function of the trail and the window: it keeps no memory of what it raised, so
overlapping scans raise twice. Deduplication belongs to whatever persists incidents, which is the only
thing that knows what is already open. The incident id is derived from what triggered it so that it can.

## Zero dependencies

Deliberate. Escalation belongs to whatever the deployment already uses for notifications, and pulling
that in here would tie a module about deadlines to a delivery mechanism. The audit bridge is a separate
package for the same reason.

## Packages

| Package | For |
|---------|-----|
| `Pragmatic.Incidents` | `SecurityIncident`, `IncidentDeadlines` — no dependencies |
| `Pragmatic.Incidents.Audit` | `AuditPatternDetector`, `DetectionRule` — detection over the `Pragmatic.Audit` trail |

## Status

**Preview** within 1.0.0-alpha — incident records and the reporting clocks that run against them; one
reference application (Time off) uses them. See the [roadmap](../docs/ROADMAP.md).

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Incidents is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
