# Pragmatic.Privacy

Erasure, retention, consent, access and portability — the parts of GDPR that have entities, policies and
a lifecycle.

## The shape

Classification is a **trait on the entity**; the process is a **module**. The dichotomy the design
started from — trait *or* service — was false. The attribute declares what a property is, the source
generator turns that declaration into an executor, and the module owns the workflow that runs it.

```csharp
[DataSubject(nameof(Email))]
public class Customer
{
    public string Email { get; set; } = "";

    [PersonalData(DataCategory.Contact, Erasure = ErasureStrategy.Null)]
    public string? Phone { get; set; }

    [PersonalData(DataCategory.Financial, Erasure = ErasureStrategy.Retain,
        Reason = "Statutory retention of invoices, 10 years")]
    public string? Iban { get; set; }
}

[LinksToSubject(nameof(Customer))]
public class Order { public Customer Customer { get; set; } = null!; }
```

The generator reports what is missing rather than guessing: an entity with personal data and no path to
a subject (PRAG2900), `Retain` without a reason (PRAG2901), `DestroyKey` on something unencrypted
(PRAG2902), an unclassified string on an entity a subject reaches (PRAG2903), special-category data
behind an endpoint that names nobody (PRAG2904), a `[LinksToSubject]` path that cannot be followed
(PRAG2906).

**Nothing is reported until a `[DataSubject]` exists.** PRAG2903 asks for a decision about every
unclassified string; across a solution that has not opted in that is noise, and noise is how a real
finding gets ignored.

**What an entity owns is part of what it holds.** The reader descends into an owned record — an
identity record, a value object — and names its columns by the path to them: `Identity.Email` in the
register, in the erasure plan, in the export and in PRAG2903. An owned record is analysed through its
owner and not as an entity of its own, so it needs no `[LinksToSubject]`: its owner has the path. The
descent stops where another entity begins (`[Entity]`, `[DataSubject]`, `[LinksToSubject]` — each has
its own plan), at a collection, and four levels down.

**And so is what it inherits.** The reader walks the base chain, stopping outside `System.*`, and counts
a member hidden with `new` once — from the derived type, which is the declaration the compiler binds. A
base class is not an entity of its own either, so it is folded in the same way an owned record is.

## Quick start

```csharp
services.AddDbContext<PrivacyDbContext>(o => o.UseNpgsql(connectionString));
services.AddSubjectRegistry();   // the store: registry + consent
services.AddPrivacy();           // the processes: erasure, access, portability, Article 30
```

The registry also needs an `ISecretEncryptor` for the identities it stores and an
`ISubjectLookupKeyProvider` for the blind index it searches by — keys the application supplies, and
never from the database the table is in.

**Where the tables come from.** In a Pragmatic host the migration of the boundary that holds a
`[DataSubject]` creates the registry's tables (`__Subjects`, `__Consents`) beside that boundary's data, once
the host references `Pragmatic.Privacy.EFCore`: the generated context applies
`PrivacyDbContext.ApplyPrivacyConfigurations`, as it applies the audit trail's for an `[Audited]` entity.
`PrivacyDbContext` then reads and writes those same tables — and in that host the first two lines above
are not written either: the generated host registers `PrivacyDbContext` on that database and calls
`AddSubjectRegistry()`, unless the application registered the context itself. The keys stay yours.

**From an operation**, depend on the interfaces — `ISubjectAccess`, `ISubjectErasure`,
`IProcessingRegisterBuilder` — which resolve to the same instances as `SubjectAccessService`,
`ErasureOrchestrator` and `ProcessingRegisterBuilder`. The generator does not inject a concrete type into
an action (PRAG0419).

You still contribute the parts only the application knows — `IErasureStep` (what erasure touches),
`IPersonalDataSource` (what an access request collects), `IProcessingActivitySource` (what the register
describes), and `ILegalHoldStore` when holds apply. Each is an enumerable, so adding one is a
registration rather than a replacement.

With the source generator, the first three arrive on their own: classifying a field generates the
adapters and the activity source, and the generated `AddGeneratedPrivacyAdapters()` — which the host
calls for you — contributes them and calls `AddPrivacy()` itself. The call above is what an application
supplying its own implementations writes; it is `TryAdd` throughout, so writing it anyway changes
nothing.

`ConsentAwareRetentionResolver` is **not** registered for you: it needs the notice version currently in
force, and a wrong default would evaluate consent against a notice the subject never saw while looking
like it worked.

`ISubjectRegistry` and `IConsentStore` **are** registered for you, with implementations that throw on
every call and name what to register instead. The point is separability: the Article 30 register uses
neither, so classifying a field must not oblige you to stand up a database, an encryptor and a lookup
key before the application will start. Without them the choice was between refusing to boot and an
access request that answered "no data" — and the second is the one that reaches production.

## Things that are less obvious than they look

**An identity that returns after erasure is a new subject.** Recognising it would require keeping
something derived from the identity, which is still processing their data. There is no
"already forgotten" answer, on purpose.

**Retention belongs to the lawful basis, not the type.** The same email address is kept for different
periods depending on why it was collected, so `[PersonalData]` carries classification and
`IRetentionPolicyResolver` carries policy.

**Consent is per (subject, purpose, notice version).** The notice version is in the key: consent to a
notice nobody can reproduce is not consent. Revocation does not delete the record, and is not an
erasure request.

**The Article 30 register declares what it is missing** rather than filling gaps in — a register that
looks complete and is not is worse than one that says where it is thin.

**A purpose belongs to an operation, not to a type.** The register has two halves: `Activities`, one per
type holding personal data, and `ProcessingOperations`, one per operation that touches it. Asking why
`Member.DisplayName` is held has no single answer, because a search, an invitation and a rectification
touch it for different reasons; asking why `ListMembersQuery` runs has one. Declare them in
`ProcessingRegisterOptions.OperationPurposes`, and read what is still undeclared from
`IncompleteOperations`. A query and a mutation name their entity; a **domain action** does not, and its
entities are derived from the dependencies it declares — an `IRepository<T>` or `IReadRepository<T>` it
holds, or an `IMutationInvoker<TMutation, TEntity>` it composes. What an action or a mutation **loads** is
derived too: `[LoadEntity<T>]`, `[LoadEntities<T>]`, the entity of a `[LoadFrom<TQuery>]` query, and the
`[PragmaticUser]` entity for `[LoadCurrentUser]`. One that reaches data any other way stays out rather
than being listed against a guess — `[ProcessesData<T>]` is for that, and only for that: one naming an
entity the generator already derives is `PRAG2913` (Info), unless the operation also composes through a
boundary interface, where the declaration may be answering for the composition.

**Reads are recorded only where an operation asks.** Writes reach the audit trail through an interceptor
over `SaveChanges`, which never sees a query, so nothing records a read. Recording them all is the wrong
default — reads outnumber writes by orders of magnitude, and a trail holding all of them cannot be
searched when it matters — so a query opts in with `[RecordAccess]`, and the entry carries the
operation, the actor and the time, never the rows. `ProcessingRegister.UnrecordedReads` lists the rest,
because "who looked at this person's record" cannot be answered retroactively: the evidence was written
at the time or it does not exist.

**The trail names the operation, not only the change.** Every audit entry carries `BusinessOperation` —
the fully qualified query, mutation or action that was running — which is the same key the register uses.
Without it the register described what *can* touch personal data and the trail recorded that something
did, with no way to join them. It is read from an ambient the invokers set, deliberately not from
`Activity.Current`: `StartActivity` returns null when no listener is registered, and a field populated
only where tracing happens to be on is worse than one never populated, because only the second is
noticed.

**Under a legal hold the subject's key is not destroyed**, or it would render unreadable the very data
the hold exists to preserve.

## Encryption and erasure

Erasure by key destruction relies on `Pragmatic.Cryptography`: each subject has a key, and destroying it
makes their data unreadable everywhere it was copied — backups included, which is the only way to erase
a copy you do not control. See [that module's README](../Pragmatic.Cryptography/README.md), and
[the compliance threat model](../docs/security/threat-model-compliance.md) for what this does and does
not protect against.

## Packages

| Package | For |
|---------|-----|
| `Pragmatic.Privacy.Abstractions` | The classification attributes (`[PersonalData]`, `[DataSubject]`, `[LinksToSubject]`, …) — no dependencies, so entity assemblies can reference it alone |
| `Pragmatic.Privacy` | The processes: erasure, access, portability, the Article 30 register (`AddPrivacy()`) |
| `Pragmatic.Privacy.EFCore` | The subject registry and consent store over `PrivacyDbContext` (`AddSubjectRegistry()`) |

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer (the adapters, the activity source, the PRAG29xx diagnostics)

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Privacy is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
