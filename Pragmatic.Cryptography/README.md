# Pragmatic.Cryptography

Key management and encryption at rest for the Pragmatic.Design ecosystem.

## The Problem

Encrypting a value at rest is the easy part — `AesGcm` is three lines. Living with it is not. The key
has to come from somewhere that is not the store holding the ciphertext. It has to be replaceable
without a flag day, which means the ciphertext must say *which* key produced it. And a value must not
be movable from one record to another and still authenticate.

Get any of those wrong and the failure is silent: the code encrypts, the tests pass, and rotation turns
out to be impossible the day you need it.

```csharp
// Without this package: one key, forever. Rotating it means re-encrypting everything
// in a single outage, because nothing records which key wrote which value.
using var aes = new AesGcm(key, 16);
aes.Encrypt(nonce, plaintext, ciphertext, tag);
```

## The Solution

AES-256-GCM behind `ISecretEncryptor`, with a **versioned, key-tagged ciphertext format**:

```
[0x01][keyIdLen][keyId][nonce 12][tag 16][ciphertext]
```

The embedded `keyId` — a deterministic 8-character fingerprint of the key material, not a name someone
has to assign — lets a reader pick the exact key from the ring. That is what makes rotation possible:
the current key encrypts new values while previous keys still decrypt old ones.

```csharp
var ring = new EncryptionKeyRing(
    current:  EncryptionKey.FromMaterial(newKey),
    previous: [EncryptionKey.FromMaterial(oldKey)]);   // still decrypts what it wrote

using var encryptor = new AesGcmSecretEncryptor(ring);

// Associated data binds the ciphertext to the record's identity: a value moved to another
// record fails authentication instead of decrypting into the wrong place.
byte[] packed = encryptor.Encrypt(value, associatedData: $"{tenantId}:{key}");
string back   = encryptor.Decrypt(packed, associatedData: $"{tenantId}:{key}");
```

## Packages

| Package | For |
|---------|-----|
| `Pragmatic.Cryptography` | `ISecretEncryptor`, `AesGcmSecretEncryptor`, the key ring and key providers, `ISubjectDataProtector`, `ProtectedValue` |
| `Pragmatic.Cryptography.EFCore` | Per-subject keys: `CryptographyDbContext`, `AddCryptographySubjectKeys()`, `ProtectedValueConverter` |

## Features

- **AES-256-GCM** — authenticated encryption; tampering fails the tag check rather than yielding garbage.
- **Key ring with rotation** — current key plus any number of previous keys, selected per value by the
  embedded key id.
- **Deterministic key ids** — the fingerprint is derived from the material (`SHA-256`, first 4 bytes),
  so no manual naming and no id/key mismatch.
- **Associated data (AAD)** — binds ciphertext to the identity of what it belongs to.
- **Pluggable key sources** — `IEncryptionKeyProvider` / `IEncryptionKeyRingProvider`. This package
  ships `EnvironmentEncryptionKeyProvider`; config-bound providers belong to the module that owns the
  configuration.
- **Key material zeroed on dispose** — keys do not outlive the encryptor in managed memory.

## Per-subject keys (`Pragmatic.Cryptography.EFCore`)

One key per subject, resolved by the id the ciphertext carries. This is what makes **crypto-shredding**
possible: destroy the key and that subject's data is unreadable everywhere it exists — including in
backups, which no `DELETE` can reach.

```csharp
services.AddCryptographySubjectKeys();          // needs CryptographyDbContext + a master ISecretEncryptor

var key = await store.GetOrCreateAsync(subjectRef);   // created on first use
…
await store.DestroyAsync(subjectRef);                 // the data is now unreadable, permanently
```

The subject reference is an **opaque pseudonym, never an identity** — this package has no idea what a
subject is, which keeps it usable for anything that needs per-entity keys.

Two properties worth knowing before you rely on it:

- **A destroyed subject is never resurrected.** `GetOrCreateAsync` throws rather than minting a new key,
  because writing new data under a reference already reported as erased would make that report a lie. A
  subject who returns gets a *new* reference.
- **The row outlives the key.** Destruction clears the wrapped key and stamps the time, but keeps the
  key id — that is what lets a later read tell *erased* apart from *tampered with*. Without it every
  read of erased data would look like an attack, and a real attack would be lost in the noise.

Subject keys are stored wrapped by the master key ring, with the subject reference as associated data,
so a wrapped key lifted from one row cannot be planted on another.

> **Sharing a database with another Pragmatic module?** Do not create the schema with
> `EnsureCreated()`. It is all-or-nothing per *database*, not per context: the first module creates the
> database and its tables, the second finds the database already there and returns without creating
> anything — leaving a half-built schema and no error at all. Use migrations, or apply both modules'
> configurations to a single `DbContext` (`ApplyCryptographyConfigurations`, and the equivalent on the
> other module).

### Reading: three outcomes, not two

```csharp
var result = await protector.TryReadAsync(packed);

result.Outcome switch
{
    DecryptOutcome.Success              => Use(result.Plain),
    DecryptOutcome.KeyDestroyed         => Erased(),      // expected; not a security event
    DecryptOutcome.AuthenticationFailed => Alert(),       // this one is
};
```

Two outcomes would not be enough once keys can be destroyed. Every legitimate read of erased data fails
to decrypt, so reporting it the same way as a bad authentication tag means either erased records look
like attacks, or real attacks disappear into the noise of ordinary erasures — and which one you get
depends on how many subjects have been erased. That is not a way to run a security signal.

### Caching, and why a stale cache is harmless

`SubjectKeyCache` caches unwrapped **key material only** — never whether a subject still exists. Every
read asks the store for the subject's status first, so a destroyed key is reported as erased before the
cache is consulted at all.

This is what makes multi-instance deployments safe without distributed invalidation: if one instance
destroys a key, another instance's warm cache cannot resurrect the data, because that instance checks
the status too. Local eviction on destruction still happens — it just isn't what you are relying on.

> `IKeyResolver` on its own is **not** an erasure-safe path: it resolves material, it does not check
> that the subject still exists. Read subject data through `ISubjectDataProtector`.

### Storing protected data

A protected column is a `ProtectedValue` — the packed ciphertext as it sits in storage — mapped to
`byte[]` by `ProtectedValueConverter`.

```csharp
b.Property(c => c.Email).HasConversion<ProtectedValueConverter>();
```

**That converter performs no cryptography, on purpose.** Encryption and decryption stay explicit:

```csharp
customer.Email = new ProtectedValue(await protector.ProtectAsync(subjectRef, bytes));
…
var read = await protector.TryReadAsync(customer.Email.Packed);
```

Two reasons a transparently-decrypting converter would be wrong, and one reason this is better anyway:

1. A value converter is a compiled expression invoked **synchronously** inside EF's pipeline, while
   resolving a subject's key is a database read.
2. A converter can return a value or throw. It cannot say *"this subject was erased"* — so making it
   decrypt would turn an expected outcome back into something that looks like an attack, undoing the
   three-outcome result above.
3. Hidden decryption is exactly the invisible convention this codebase avoids: the reader of a query
   would see neither the key lookup, nor the erased case, nor the cost.

After erasure the row is untouched and unreadable — which is the point:

```csharp
loaded.Email.IsEmpty      // false — the ciphertext is still there
await protector.TryReadAsync(loaded.Email.Packed)   // KeyDestroyed
```

## Legacy values

Ciphertext written before the versioned format existed has no header. It is detected by falling
through: GCM authentication makes a mis-framed decrypt fail its tag check, so the reader safely tries
each ring key under the legacy framing. New writes always use the versioned format.

## What this package does not do

- It is **not a crypto library** — it uses `System.Security.Cryptography`, it does not implement primitives.
- It does **not decide what to encrypt**. That is a classification concern and belongs to the caller.
- It does **not manage TLS or certificates** — those are host configuration.
- It is **not a KMS**. It integrates with an external secret store as a key source; it does not replace one.
- It cannot protect against a process that already holds the keys in memory.

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Cryptography is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
