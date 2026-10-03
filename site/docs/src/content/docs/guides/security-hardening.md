---
title: "Security Hardening"
description: "A guide to securing an application built with Pragmatic.Design. It covers the security model of every relevant module and the actions that fall to the consumer."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/docs/howto/security-hardening.md
sidebar:
  order: 5
---
A guide to securing an application built with Pragmatic.Design. It covers the security model of every relevant module and the actions that fall to the consumer.

To report a vulnerability: see [`SECURITY.md`](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/SECURITY.md). Do not open public issues.

## Principle: fail-closed

Pragmatic modules adopt fail-closed defaults where security is at stake:

- `Endpoints`: `RequireAuthorizationByDefault` is `true`, so the generated root group applies `RequireAuthorization()` and an endpoint without attributes requires authentication. With `DefaultAuthorizationPolicy` set it applies `RequireAuthorization(policy)`, that is, that policy instead of just an authenticated user. Opt out case by case with `[AllowAnonymous]`.
- `Actions`: the filter pipeline (Validation → Permission → Policy → Resource) denies if an authorizer does not grant; `AnonymousUser` has `NullUserAuthorization` (deny-all).
- `Email`: sending refuses to authenticate if `UseSsl` is required but TLS was not established.
- `Migrations`: breaking changes (DROP) are blocked unless `Force=true` is set explicitly.

## Identity & authentication

### JWT (`Pragmatic.Identity.Local.Jwt`)

- **Signing key**: at least 32 bytes (256 bits) for HMAC-SHA256, validated by `UseJwtAuthentication`. Generate a random key, not a passphrase.
- **Issuer/Audience**: in the Production environment both **must** be configured, otherwise `UseJwtAuthentication` throws. Without them, the validator would accept tokens from any issuer/audience.
- **Algorithm**: fixed HMAC-SHA256, no support for `alg: none`.
- **Secrets**: the signing key comes from a secret manager (Key Vault, env var, user-secrets), never hardcoded nor in a committed `appsettings.json`.
- **Revocation**: JWT tokens are stateless, so they cannot be revoked before they expire. Keep `TokenExpiration` short (default 1h). Immediate revocation needs an external blocklist store (consumer side).

### Local login (`Pragmatic.Identity.Local`)

- **Per-account lockout**: enabled via `MaxFailedLoginAttempts` + `LockoutDuration` in `LocalIdentityOptions`. Configure values that fit your risk.
- **IP-level rate limit**: not included by default. For defence in depth against volumetric spraying, mount ASP.NET Core's `AddRateLimiter`/`UseRateLimiter` on the login endpoint.
- **Development handlers**: `NoOpAuthenticationHandler` and `HeaderUserMiddleware` are **for development only**. Check that the Production configuration uses `UseJwtAuthentication`.

### Authorization

- The permission cache (`UsePermissionCache`) has a TTL: a role/permission change at runtime takes effect at the latest after the TTL. For immediate propagation, lower the TTL or invalidate explicitly.
- `internal call`s in the Actions pipeline bypass the permission check (they are in-process, same trust boundary) and leave no audit trail: take this into account in your audit model.

## Persistence

- **Data visibility**: use `[HasOwner]`/`[HasAccessScopes]` instead of manual filters; the SG generates the query filters and the permission-based bypasses.
- **`[WithoutFilter]`**: disables the query filters for a type. It is a **privilege**, not an escape hatch: apply it only to queries in already-authorized contexts (admin/background), never to work around an inadequate filter.
- **Connection strings**: from the secret manager, never logged. Pragmatic does not log them.

## File storage (`Pragmatic.Storage`)

`LocalDiskFileStorage` is meant for development/demo. In any case:

- **Path traversal**: the `container` parameter is validated, and a value that resolves outside `{basePath}/files/` is refused. Reads (`GetAsync`/`DeleteAsync`) also stay confined to the root.
- **Size limit**: pass `maxFileSizeBytes` to the constructor to refuse uploads over the threshold (it applies to non-seekable streams too). Default `0` = unlimited: **set a limit in production**.
- **Content type / extension**: validating the type of the uploaded file is the consumer's responsibility. Do not trust the content type declared by the client.
- In production use a dedicated backend (Azure Blob, S3) served behind a CDN, not the host's local disk.

## Email (`Pragmatic.Email`)

- **TLS**: with `UseSsl = true` the connection refuses to authenticate if STARTTLS is not available, so credentials never travel in clear text. Keep `UseSsl = true` in production.
- **Header injection**: header values (subject, display name, custom headers, attachment name) are sanitized/encoded, so a `\r\n` cannot inject headers. Invalid custom header names are refused.
- **SMTP credentials**: from the secret manager.

## Messaging

- **Transport credentials**: the RabbitMQ connection string is not logged in clear text (only `host:port/vhost`). Keep it in a secret manager anyway.
- **Outbox**: delivery is **at-least-once**, so a consumer can receive the same message more than once. Handlers **must be idempotent** (e.g. dedup on `MessageId`).
- **Deserialization**: message types are resolved through the registry generated by the SG (FQN → Type), not through `Type.GetType()` on arbitrary input.

## Logging

- **Log forging**: values that end up in the log message have CR/LF neutralized, so user input cannot inject fake log lines.
- **Sensitive data**: do not log passwords, tokens, PII. ⚠️ `[NotLogged]` **only marks; nothing enforces it today**: the SG derives a redaction map for message types from it, but nothing consults it (message audit does not record the payload). Actual redaction is **by pattern**, not by marking (`PragmaticDataRedactor` on the configured property-name patterns and `PersonalDataRedactor` on the audit trail), so a field called `Pwd` passes if no pattern covers it. For declared personal data use `[PersonalData]` (Pragmatic.Privacy), which is wired end to end.

## Gateway (`Pragmatic.Gateway`)

- **CORS**: the combination `Origins: ["*"]` + `AllowCredentials: true` is invalid and is refused at startup with a clear message. For credentials, list explicit origins.
- Terminate TLS at the gateway; internal routes assume a trusted network.

## ControlPlane / Agent

- **Agent socket** (Unix): the socket is `0600` and its directory `0700`; the trust boundary is the uid of the agent process. Do not loosen these permissions.
- **ControlPlane hub**: command payloads are validated (non-empty, size-limited, well-formed JSON). Access stays protected by an API key: expose it only on an internal network and always over HTTPS.

## Remote boundaries (`/_pragmatic/invoke`)

The generated `POST /_pragmatic/invoke` endpoint is internal boundary-to-boundary RPC. It is **fail-closed by default**: without configuration it requires an authenticated principal. The generated remote client **propagates the caller's identity** by forwarding the `Authorization` header on the outgoing call (the `PragmaticRemoteAuthHandler` handler registered on every named client).

- **Default (recommended)**: no configuration → the endpoint requires authentication, and the caller's identity is propagated. A call without a credential is refused.
- **Service policy**: set `Pragmatic:RemoteBoundaries:InvokeEndpoint:AuthorizationPolicy` to the name of a registered policy for a stricter service-to-service check.
- **Trusted-network opt-out**: `Pragmatic:RemoteBoundaries:InvokeEndpoint:AllowAnonymous: true` makes the endpoint anonymous; use it **only** on an isolated network. It is never silently anonymous: the opt-out is explicit and traceable in configuration.

> ⚠️ If your hosts communicate from contexts without an HTTP context (jobs/background), there is no header to propagate: configure an explicit service credential, or `AllowAnonymous` on a trusted network.

## Pre-production checklist

- [ ] JWT: signing key ≥256 bits from a secret manager; Issuer + Audience configured; short `TokenExpiration`.
- [ ] Production authentication = JWT (not `NoOp`/header).
- [ ] Login lockout configured; IP rate limit on the login endpoint.
- [ ] `Storage`: upload size limit set; production backend (not local disk).
- [ ] `Email`: `UseSsl = true`; credentials from a secret manager.
- [ ] Messaging: idempotent handlers (the outbox is at-least-once).
- [ ] Connection strings and secrets outside committed configuration files.
- [ ] CORS: explicit origins if credentials are needed.
- [ ] HTTPS everywhere; ControlPlane/Agent only on an internal network.
- [ ] Remote invoke (`/_pragmatic/invoke`): left fail-closed, or `AllowAnonymous: true` **only** on an isolated network and documented.
- [ ] Migrations: no `Force=true` by default; a backup before every destructive migration.
