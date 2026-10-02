# Pragmatic.Identity.Auditing

Records identity security events — failed logins, account lockouts — on the framework audit trail.

A bridge, like `Pragmatic.Messaging.Auditing` and `Pragmatic.Incidents.Audit`: identity has no business
knowing about the trail, and the trail none about logins.

## Use

```csharp
services.AddIdentitySecurityAuditing();
```

The trail itself comes from `AddAuditTrail()`, and the subject registry from `AddSubjectRegistry()` — both
called by a Pragmatic host whose entities are `[Audited]` / `[DataSubject]`.
Without a registry every entry is written with no subject — blunter, but working rather than broken.

## The attempted address never reaches the trail

`LoginFailed` carries it in plaintext, deliberately, so security tooling can correlate attempts. Its own
documentation hands the data-protection obligations to whoever persists it; this package is that owner.

The address is resolved through `ObservedIdentityResolver`, which **looks up and never allocates**. An
attempt against a known account carries that account's pseudonym and can be correlated with its
history; an attempt against an identity nobody recognises is recorded with **no subject at all**.

Pseudonymising the attempted address instead — the obvious implementation — would let anyone fill the
subject registry by guessing, and would have the system create personal data about people as a side
effect of rejecting them.

Unattributable failures are still written. A burst against accounts that do not exist is what
enumeration looks like, and it is often the more interesting signal precisely because it cannot be
attributed.

## A lockout is Denied, not Failed

The credentials were not rejected; the account was closed to attempts. A reader filtering for genuine
authentication failures should not find lockouts mixed in.
