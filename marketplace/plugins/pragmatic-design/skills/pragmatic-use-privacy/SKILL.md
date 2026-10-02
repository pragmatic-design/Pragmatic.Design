---
name: pragmatic-use-privacy
description: Use when the app stores data about a person and must answer a deletion or export request or keep a processing register, or when PRAG2900-2913 fire — Pragmatic.Privacy, [PersonalData], erasure plan, crypto-shredding.
---

# Pragmatic Use Privacy

**Covers:** Classify personal data with Pragmatic.Privacy — [DataSubject], [PersonalData], [LinksToSubject], the generated erasure plan and processing register, access and erasure requests, legal holds, crypto-shredding (Pragmatic.Cryptography).

Personal data is classified **where it is declared**, and everything else — the erasure plan, the
export handed to the subject, the processing register — is derived from that one declaration. The
point is not the attributes: it is that a register derived from the code cannot drift away from what
the code does, which is the usual state of a processing register.

## The three declarations

```csharp
[Entity]
[DataSubject(nameof(ExternalId))]                        // the person everything hangs off
public partial class Member : IEntity
{
    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Retain,
        Reason = "Referenced by audit entries; the link to a person is severed by clearing DisplayName.")]
    public string ExternalId { get; private set; } = "";

    [PersonalData(DataCategory.Identity, Erasure = ErasureStrategy.Anonymize)]
    public string DisplayName { get; private set; } = "";

    [NotPersonalData("Tenant isolation key, assigned by the platform.")]
    public string TenantId { get; set; } = "";
}
```

| | |
|---|---|
| `[DataSubject(idProperty)]` | the person. **Declaring one is what switches the analysis on** — before that the generator emits nothing and reports nothing, because warning about unclassified strings in an app that has not opted in is noise nobody can act on |
| `[PersonalData(category, Erasure =, Reason =, Encrypted =)]` | classification, not policy. Retention belongs to the legal basis, not the type: the same row is kept ten years under a fiscal obligation and until withdrawal under consent |
| `[NotPersonalData(reason)]` | a recorded **no**. The reason is mandatory and appears in the register beside the classified fields |
| `[LinksToSubject(pathProperty)]` | how an entity reaches its subject when it is not the subject itself |
| `[RecordAccess]` | on a **query**: writes an audit entry each time it succeeds. Off everywhere else, and that is the right default — reads outnumber writes by orders of magnitude, and a trail holding all of them cannot be searched when it matters. The entry carries the operation, the actor and the time, never the rows |

`DataCategory`: `Identity`, `Contact`, `Financial`, `Location`, `Behavioural`, `Special`.
`ErasureStrategy`: `Null`, `Delete`, `Anonymize`, `Pseudonymize`, `Retain`, `DestroyKey`.

**From the first `[DataSubject]` the blast radius is every entity reachable from it** — and the
analysis then demands a ruling on each of their string properties. That is the point, and it is also
the surprise: expect to classify more than you meant to on the first build.

**The subject registry is wired by the host.** With `Pragmatic.Privacy.EFCore` referenced, the migration
of the `[DataSubject]`'s database creates its tables and the generated host registers `PrivacyDbContext`
there and calls `AddSubjectRegistry()` — do not write either in `Program.cs`. What stays yours are its
keys: an `ISecretEncryptor` and an `ISubjectLookupKeyProvider`, from configuration, never from that
database.

⚠️ **Allocate the pseudonym where the person enters the system, not where they first act.** A handler
on the registration event calling `GetOrCreateReferenceAsync` — it is idempotent, so the later call
sites cost a lookup and nothing else. Allocating lazily, at first sign-in or first export, leaves a
window in which an account exists and cannot be named by anything that records what happens to it: the
failed sign-ins against an account nobody has used yet are written with no subject, and a per-subject
rule sees nothing. That is the account an attacker works on. ⚠️ The security bridge cannot close this
for you and is right not to: its input is an address a stranger typed, and allocating there would let
anyone fill the registry by guessing.

A mutation's `[Raises<T>]` event is built after the save, so a `[GeneratedValue]` key such as
`EMP-00001` is already in it: the registration handler can take the subject's id from the event.

## The diagnostics, in the order you will meet them

| | |
|---|---|
| **PRAG2900** ⛔ | personal data with no path to a `[DataSubject]` — those rows can never be erased. A classification nobody can act on is worse than none: it reads like compliance |
| **PRAG2903** ⛔ | an unclassified string on a subject-reachable entity. Classify it, or `[NotPersonalData]` it. The decision is what is being asked for — an unclassified field is indistinguishable from one nobody thought about. ⚠️ It asks about what the entity **owns** and what it **inherits** too, named by the path: `Identity.ResetToken`, not `ResetToken`. An owned record and a base class are read through the entity that holds them — no `[LinksToSubject]` on either, and their columns appear in the register, the plan and the export under the same path. Classify them **where they are declared**, which for a framework type is that package |
| **PRAG2909** ⛔ | `Erasure = Null` on a non-nullable property. A plan that throws at the moment someone exercises a right is worse than no plan; use `Anonymize` |
| **PRAG2912** ⛔ | `Anonymize` on a non-nullable type with no anonymous value. A string becomes `""`, a value type its default, a nullable property null; a `byte[]` or an owned object has no value that is both anonymous and generic, so make it nullable or choose another strategy |
| **PRAG2901** ⛔ | `Retain` without a `Reason`. "We keep it" is not an answer to give a data subject |
| **PRAG2902** ⛔ | `DestroyKey` on a property that is not `Encrypted` — destroying a key that protects nothing reports an erasure that did not happen |
| **PRAG2907** ⛔ | an erasure strategy nothing can write, because the setter is not public. The plan would leave the field untouched and declare it erased |
| **PRAG2906** ⛔ | `[LinksToSubject]` naming a property that is not a navigation |
| **PRAG2908** ⚠️ | the `[DataSubject]` identifier is not a `string`, `Guid`, `int` or `long`. The registry hands an identity back as a string, and nothing can compare another type against it — so no export source and no erasure step is generated for that subject until you write `IPersonalDataSource`/`IErasureStep` by hand |
| **PRAG2904** ⚠️ | an endpoint exposing `DataCategory.Special` without saying who may read it |
| **PRAG2910** ⚠️ | `[RecordAccess]` where the assembly does not reference `Pragmatic.Audit`, so nothing is written. The register reports the operation as unrecorded, and the two agree |
| **PRAG2911** ⚠️ | an operation that composes through a boundary interface and declares no `[ProcessesData]`. The register derives what an operation reaches from the **types of its dependencies** — `IRepository<T>`, `IReadRepository<T>`, `IMutationInvoker<TMutation, TEntity>` — and from its **loads**, and `I{Boundary}Actions` names no entity. So an operation written the way this framework asks for it disappears from the Article 30 register while it goes on writing the same rows |
| **PRAG2913** ℹ️ | a `[ProcessesData<T>]` naming an entity the generator already derives — from a dependency or a load (`[LoadEntity]`, `[LoadEntities]`, `[LoadFrom]`'s query, `[LoadCurrentUser]`). Remove it: two sources for one fact drift. Not reported on an operation that also composes through a boundary interface, where it may be answering PRAG2911 |

⚠️ Most are **errors**, and deliberately: each describes a plan that would fail, or lie, at the moment
it runs. That is the one moment where failing is most expensive.

`PRAG2905` is deliberately unused — encrypted transport is a property of the deployment, not
something decidable at compile time.

### Answering PRAG2911: `[ProcessesData]`

⚠️ **This is the one you will meet without asking for it**, because composing through the boundary is
what the rest of this framework tells you to do. An action that injects `I{Boundary}InternalActions`
and calls two mutations through it reaches two entities, and nothing in its dependencies says so.

```csharp
[DomainAction]
[ProcessesData<KnowledgeItem>]     // what this route actually touches, named because
[ProcessesData<TermCandidate>]     // the boundary interface cannot say it
public partial class PromoteTermCandidateAction : DomainAction<Guid, TermCandidateNotFoundError>
{
    private IKnowledgeInternalActions _knowledge = null!;
}
```

The generic form is the one to reach for: it names the entity, and the register lists the operation
against it. It is **not** for what the generator derives: a repository the action holds, and every load
(`[LoadEntity]`, `[LoadEntities]`, `[LoadFrom<TQuery>]`, `[LoadCurrentUser]`), are in the register already —
declaring them again is PRAG2913. The non-generic `[ProcessesData]` says "reviewed, and it reaches no classified data" —
use it where that is true and you want the diagnostic to stop asking.

⚠️ **Declare it on every operation that composes, not only where a test went red.** The register
filters out entities carrying no personal data, so an entity nobody has classified yet changes
nothing today — and the day somebody classifies it, the register is already right. Writing the
attribute only where something complained is writing to the test instead of to the rule.

## What gets generated

Once a subject exists: the erasure plan and its steps, the personal-data source and extractor for the
export, the processing-activity source for the register, plus DI registration. Nothing to write by
hand, and nothing to keep in sync.

## Answering a request

The generated steps and sources are registered by the host (with `AddPrivacy()`); what a request needs is
an operation of yours that depends on the interfaces — never the concrete types (`PRAG0419`):

| Right | Inject | Returns |
|---|---|---|
| access / portability | `ISubjectAccess.CollectAsync(subjectRef)` | `SubjectDataExport`; `IPortabilityFormatter` (JSON by default) turns it into the file handed over |
| erasure | `ISubjectErasure.EraseAsync(subjectRef, destroyKeyAsync)` | `ErasureOutcome` — `ErasedCount`, `Retained` (what was kept, and why), `KeyDestroyed`, `IdentityForgotten` |
| the Article 30 register | `IProcessingRegisterBuilder.BuildAsync()` | the register as the code stands |

- The argument is the subject's **pseudonym** from the registry, never an e-mail.
- `Retained` non-empty is the ordinary answer, not a failure: "12 records erased, 2 kept under a fiscal
  obligation" is complete. What would be wrong is keeping without saying so — hand `Retained` to the
  subject.
- **Legal holds** — a live dispute that suspends erasure — are an `ILegalHoldStore` you register; they are
  checked before anything is touched, so a hold never leaves a subject half-erased.
- Consent-based retention (`ConsentAwareRetentionResolver`) is not registered for you: it needs the
  version of the notice you publish, and there is no safe default for it.

## Encrypted fields and crypto-shredding

For data that must become unreadable **everywhere**, backups included, encrypt it under the subject's
own key (`Pragmatic.Cryptography` + `Pragmatic.Cryptography.EFCore`) and erase by destroying the key:

```csharp
[PersonalData(DataCategory.Special, Encrypted = true, Erasure = ErasureStrategy.DestroyKey)]
public ProtectedValue? MedicalNotes { get; set; }     // the converter is applied by the generated config
```

- Writing: `entity.MedicalNotes = new ProtectedValue(await protector.ProtectAsync(subjectRef, bytes))`
  (`ISubjectDataProtector`; the key is created on first use).
- Reading has **three** outcomes: `TryReadAsync(packed)` → `Success`, `KeyDestroyed` (erased — expected,
  not an alarm), `AuthenticationFailed` (tampering — an alarm). ⚠️ So a protected field is never
  returned by a generated projection: the DTO carries what your code decrypted.
- Erasure: pass the destroyer, from `ISubjectKeyStore` —
  `EraseAsync(subjectRef, async (s, ct) => { await keys.DestroyAsync(s, ct); return true; }, ct)`
  (the report's `AlreadyDestroyed` says whether it had happened before). Without a destroyer, a plan
  containing a `DestroyKey` field **refuses to start** rather than report an erasure that did not happen.
  The key is destroyed unless something kept **needs** it: a legal hold does, and so does a `Retain`
  field that is itself `Encrypted` (its `RetainedItem.RequiresKey`). A `Retain` field kept in the clear
  does not — an identifier retained for the audit leaves the key free to go, and the `DestroyKey` fields
  with it. ⚠️ So a `Retain` field you also mark `Encrypted` keeps every encrypted field of the subject
  readable (`KeyDestroyed = false`): encrypt under the subject key only what should go when the key goes.
  The identity link is still kept while anything is retained — what is kept belongs to someone.
- A destroyed subject is never resurrected: writing for it again throws. A returning person gets a new
  pseudonym.
- `services.AddCryptographySubjectKeys()` needs `CryptographyDbContext` and a master `ISecretEncryptor`
  (key ring from configuration or a secret store, never from the same database). **PRAG0652** reports a
  `ProtectedValue` without `Pragmatic.Cryptography.EFCore` referenced.

## Two judgements worth making the same way twice

**Free text is not personal data — it is text that may contain some.** A story body, a note, a
description: an erasure plan cannot act on prose, so classifying it buys nothing. What it needs is
`[NotLogged]` (`Pragmatic`, in Abstractions), because the real risk is leaking into logs where nobody
is watching. `[NotPersonalData]` is for a field that is genuinely about something else — a tenant key,
a status, an external reference.

**An identifier the audit trail references is `Retain`, with the reason saying so.** Nulling it breaks
the chain that records who did what. What erasure removes is the ability to tie it to a person, which
is the display name — not the key.

## Related

- "Who did this, on whose authority" is the audit trail — `pragmatic-use-audit` — and it pairs with this
  module for anything a regulator would ask about.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Time off example application — code
that compiles and that `TimeOff.IntegrationTests` exercises — and kept identical to it by the gate: the
classification on the data subject and on a crypto-shredded field, the export and the erasure, an extra
erasure step, the read of an encrypted field, the register of processing activities, and the host's half.
