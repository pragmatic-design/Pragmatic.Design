# Threat model: the compliance stack

> Scope: `Pragmatic.Cryptography`, `Pragmatic.Audit`, `Privacy`, `Incidents`, and the source-generator
> feature that classifies personal data. Written alongside the implementation, so every mitigation below
> points at code that exists rather than at an intention.
>
> A threat model is only useful if it says what it does **not** protect against. Those are the last two
> sections, and they are the ones worth reading first.

---

## What is worth attacking

| Asset | Why someone wants it |
|---|---|
| Personal data at rest | The obvious one |
| The **master key ring** | Unwraps every subject key; holding it undoes the entire scheme |
| The **subject lookup key** | Turns the blind index back into a list of everyone in the system |
| The **subject registry** | Maps pseudonyms to people; the whole indirection collapses without it |
| The **audit trail** | Both a target to alter (hide an action) and a source to read (who did what to whom) |
| **Erased** data | Still on disk as ciphertext, and in every backup taken before the erasure |

---

## Trust boundaries

1. **Application ↔ database.** The database holds ciphertext and wrapped keys; it must not hold the keys
   that open them. Anyone with `SELECT` is inside this boundary.
2. **Application ↔ key source.** The master ring and the lookup key come from configuration, an
   environment variable, or a secret store, never from the database they protect.
3. **Process memory.** Once the process holds a key, it holds it. Nothing here defends against an
   attacker at that level.
4. **Instances of the same application.** They share a database and do *not* share caches, which is why
   nothing correctness-critical is allowed to depend on a cache being fresh.

---

## Threats and what answers them

### Reading personal data from the database

An operator, a backup, or a leaked dump. **Answered by** per-subject encryption
(`ISubjectDataProtector`) with keys wrapped by a ring stored elsewhere. Verified end-to-end against
PostgreSQL, including reading the column through raw SQL to confirm it holds ciphertext.

**Not answered** for fields the application chose to leave unencrypted. That is a deliberate trade, because an
encrypted column is neither indexable nor filterable.

### Recovering identities from the registry

The identity column is encrypted, so an attacker attacks the lookup index instead. A plain hash of an
email would fall immediately: the space of real addresses is small enough to enumerate offline.
**Answered by** an HMAC under a key that lives outside the database (`SubjectLookup.BlindIndex`), which
cannot be attacked without that key.

### Moving ciphertext between records

Lifting subject A's encrypted identity onto subject B's row. **Answered by** associated data binding
every value to its own reference: a moved value fails authentication rather than decrypting into the
wrong place. Tested in both the registry and the key store.

### Altering the audit trail

Changing, deleting or inserting an entry to hide an action. **Answered by** a Merkle root per sealed
segment, chained to its predecessor: verification names the segment that no longer matches.

**This is tamper-evident, not tamper-proof.** Anyone with write access can still alter a row; the chain
reveals it, nothing here prevents it. Prevention needs database permissions or WORM storage, which are
deployment concerns and outside this module.

### Filling the subject registry from outside

Every failed login pseudonymises the address that was tried, and an attacker submits addresses. This
would let anyone grow the registry, and would make the system hold records about people who never had a
relationship with it. **Answered by** `ObservedIdentityResolver`, which resolves only identities that
already exist and never allocates.

### Reading erased data

A key still cached after its destruction, on this instance or another. **Answered structurally**: the
cache holds key material and never subject status, and every read checks the status against the store
first, so a stale cache on another instance cannot resurrect anything. Local eviction happens too, but
is not what the guarantee rests on.

### Making an erasure look like an attack

After crypto-shredding, every legitimate read of erased data fails to decrypt. Reporting that as a bad
authentication tag would drown the real tampering signal in routine erasures: the more subjects are
erased, the less the alarm means. **Answered by** the three-outcome result: `Success`,
`KeyDestroyed`, `AuthenticationFailed`, decided by consulting the store *before* decrypting.

### Rebuilding a link an erasure removed

Recognising a returning identity requires keeping something derived from it, which is still processing
their data. **Answered by** allocating a **new** reference: an erased identity is never recognised, and
the old reference stays erased.

---

## What this does not protect against

- **An attacker with the keys.** Everything above assumes the key source is not compromised. If it is,
  the scheme provides no defence, and it is not designed to.
- **An attacker inside the process.** Keys are in managed memory while in use.
- **A malicious operator with database write access.** They can alter rows; verification will reveal it
  *if someone runs it*. Nothing forces that.
- **Personal data the application never classified.** The generator reports unclassified strings on
  entities a subject reaches, but it cannot recognise a name typed into a free-text note.
- **Backups taken before encryption was adopted.** Crypto-shredding only reaches data that was encrypted
  in the first place.
- **The application's own bugs.** A framework can make the safe path the default; it cannot stop an
  endpoint from returning data it should not.

---

## Assumptions this rests on

Each of these is a thing the deployment must be true for, not something the code can check.

1. The master ring and lookup key are stored outside the database they protect.
2. Those keys are backed up somewhere the database backups are not: losing them destroys data that no
   longer exists in any other form.
3. Somebody runs `VerifyAsync` on a schedule. An integrity chain nobody checks proves nothing.
4. Database write access is limited to the application, or the trail's evidential value is limited to
   detecting careless tampering rather than deliberate.
5. Erasure requests are verified before execution. The framework enforces the state transition; it
   cannot judge whether the verification was any good.
