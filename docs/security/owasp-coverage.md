# OWASP Top 10 — what the framework covers, and what it does not

> **How to read this.** Security is a property of every decision, not a module — so this is a map of
> where each concern is handled, with a pointer to the code. It is an assessment of *the framework*, not
> of any application built on it: several rows can only ever be "the framework gives you the tool", and
> those are marked as such rather than dressed up.
>
> **Status vocabulary.** *Covered* — the framework handles it and there is a test. *Partial* — a
> mechanism exists but does not cover the whole category. *Consumer* — the framework provides the means;
> the application decides. *Gap* — nothing today.
>
> Rows marked ⚠️ were verified by reading the code during the 2026-07 compliance work. Rows without the
> mark are placements that still need checking against the implementation — they are stated as
> *unverified* rather than assumed correct.

---

## A01 — Broken Access Control

| Aspect | Status | Where |
|---|---|---|
| Endpoints authenticated by default | ⚠️ **Covered** | `PragmaticEndpointsOptions.RequireAuthorizationByDefault` (`true`), applied by the generated root group; opt out per endpoint with `[AllowAnonymous]` |
| Remote invocation never silently anonymous | ⚠️ **Covered** | `PragmaticRemoteInvokeOptions` — anonymous must be opted into explicitly |
| Permission checks on actions | ⚠️ **Partial** | `[RequirePermission]`; a constant the generator cannot resolve would not be enforced (fail-open), and PRAG0418 reports it — as a warning, not a refusal |
| Tenant isolation | **Consumer** | `Pragmatic.MultiTenancy` interceptor + global query filters; the application must not opt out per query without meaning to |
| Row-level ownership | **Consumer** | `[HasOwner]` / `[HasAccessScopes]` filters |

**Known weakness.** The failure mode here is not a missing check but **a declared check that silently
does not run**. That is why the plan puts weight on an end-to-end security suite: unit
tests confirm a policy is configured, not that it executed.

---

## A02 — Cryptographic Failures

| Aspect | Status | Where |
|---|---|---|
| Encryption at rest for secrets | ⚠️ **Covered** | `Pragmatic.Cryptography` — AES-256-GCM, versioned key-tagged format |
| Key rotation | ⚠️ **Covered** | `EncryptionKeyRing` — current + previous keys, selected per value by embedded key id |
| Per-subject keys / crypto-shredding | ⚠️ **Covered** | `ISubjectKeyStore`; destroying the key makes data unreadable including in backups |
| Ciphertext bound to its record | ⚠️ **Covered** | Associated data — a value moved between records fails authentication |
| Encryption at rest for business data | ⚠️ **Consumer** | `ProtectedValue` + `ISubjectDataProtector`; the application chooses which fields, because an encrypted column is neither indexable nor filterable |
| Transport encryption | **Consumer** | Host configuration; HSTS emitted over HTTPS by `SecurityHeadersStep` |
| Password storage | ⚠️ **Covered** | BCrypt (`BcryptPasswordHasher`) |

---

## A03 — Injection

| Aspect | Status | Where |
|---|---|---|
| SQL parameterisation | ⚠️ **Partial** | ADO.NET paths use `AddParameter` throughout (verified in `DatabaseConfigurationStore`); EF Core parameterises by construction. **Not audited across all 65 raw-SQL sites** |
| Input validation | **Consumer** | `Pragmatic.Validation` |
| Output encoding | **Consumer** | The framework serves JSON; an application serving HTML owns its encoding |
| Deserialisation of untrusted data | ⚠️ **Covered** | Agent wire format uses a contract-based resolver with `MessagePackSecurity.UntrustedData`, deliberately not a typeless one |

---

## A04 — Insecure Design

| Aspect | Status | Where |
|---|---|---|
| Fail-closed defaults | ⚠️ **Partial** | Authorization, remote invocation and the agent's secret handling all fail closed. Not a property that can be claimed framework-wide |
| Compile-time enforcement | ⚠️ **Covered** | Diagnostics rather than runtime checks — e.g. PRAG2900-2906 make unerasable personal data a build error |
| Rate limiting | ⚠️ **Covered** | `[RateLimit]` + `PragmaticDistributedRateLimiter` |

---

## A05 — Security Misconfiguration

| Aspect | Status | Where |
|---|---|---|
| Security response headers | ⚠️ **Covered** | `SecurityHeadersStep` (Order 5, so error responses carry them); CSP defaults to denying everything |
| CORS misconfiguration | ⚠️ **Covered** | Gateway refuses wildcard origin combined with credentials, at startup |
| Body size limits | **Partial, unverified** | `RequestLimitsStep` exists; behaviour not re-read during this work |
| Antiforgery | **Partial, unverified** | `AntiforgeryStep` exists; behaviour not re-read during this work |
| Secrets in configuration | ⚠️ **Covered** | `[Sensitive]` → generated key classifier masks values in the audit log |

---

## A06 — Vulnerable and Outdated Components

| Aspect | Status | Where |
|---|---|---|
| Dependency scanning | ⚠️ **Gap** | Nothing in CI today |
| SBOM | ⚠️ **Gap** | Nothing today. Required by the CRA for published products |
| Central version management | ⚠️ **Covered** | `Directory.Packages.props`; at least one transitive pin carries a CVE reference |

**This is the largest open gap**, and the cheapest to close — it is step 0.1 of the compliance plan.

---

## A07 — Identification and Authentication Failures

| Aspect | Status | Where |
|---|---|---|
| Account lockout | ⚠️ **Covered** | `LoginUser` — failed-attempt counting and `LockoutEnd` |
| Failure events emitted | ⚠️ **Covered** | `LoginFailed`, `AccountLocked` domain events |
| Failure events **consumed** | ⚠️ **Gap** | Nothing subscribes. The detection substrate exists and is unused — step 2.7 |
| MFA — verification | **Consumer** | Delegated to the identity provider |
| MFA — enforcement | ✅ **Covered** | `ResourcePolicy.RequiresMfa()` reads `IAuthenticationContext.IsMfaAuthenticated` (the `amr` claim). Before it, the flag was populated and consulted by nothing, so MFA could be observed and never required |

---

## A08 — Software and Data Integrity Failures

| Aspect | Status | Where |
|---|---|---|
| Tamper-evident audit trail | ⚠️ **Covered** | `Pragmatic.Audit` — Merkle root per sealed segment, chained. **Evident, not proof**: it reveals modification, it does not prevent it |
| Build provenance / signing | **Gap** | Nothing today |
| Deserialisation gadget chains | ⚠️ **Covered** | See A03 |

---

## A09 — Security Logging and Monitoring Failures

| Aspect | Status | Where |
|---|---|---|
| Audit trail | ⚠️ **Covered** | `IAuditTrail` — append-only, no update or delete in the contract |
| Personal data kept out of logs | ⚠️ **Covered** | `PragmaticDataRedactor` (logging); `IAuditDetailRedactor` applied by the trail without the caller asking |
| Security events reach the trail | ⚠️ **Gap** | Step 2.7 |
| Alerting | **Consumer** | Trail + `Pragmatic.Notifications` provide the means |

---

## A10 — Server-Side Request Forgery

| Aspect | Status | Where |
|---|---|---|
| Outbound URL validation | ⚠️ **Partial** | A guard exists in `Notifications.Webhook` — resolves the host and blocks loopback, private and link-local ranges, failing closed on resolution failure |
| Shared primitive | ⚠️ **Gap** | Other URL-taking surfaces (`RemoteBoundary` base URLs, OIDC endpoints, gateway routes) do not inherit it. Extraction is open — the placement is undecided |

---

## What this table cannot tell you

A framework cannot make an application secure; it can make the secure thing the default and the
insecure thing visible. Every **Consumer** row above is a decision that stays with the application, and
no amount of framework work moves it.

The rows marked *unverified* are honest gaps in **this assessment**, not claims of absence. They should
be checked before this document is used as evidence of anything.
