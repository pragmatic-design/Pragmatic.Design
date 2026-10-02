---
title: "Architecture"
description: "Pragmatic.Email is built around four core concepts: transports, middleware, MIME generation, and security signing."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Email/docs/concepts.md
sidebar:
  order: 1
---
Pragmatic.Email is built around four core concepts: transports, middleware, MIME generation, and security signing.

## Transport abstraction

`IEmailTransport` is the pluggable interface for delivering emails:

```csharp
public interface IEmailTransport : IAsyncDisposable
{
    string Name { get; }
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
```

The library ships with four implementations:

| Transport | Purpose | Package |
|-----------|---------|---------|
| `SmtpTransport` | Production SMTP with connection pooling | `Pragmatic.Email` |
| `NullTransport` | No-op, always succeeds | `Pragmatic.Email` |
| `InMemoryTransport` | Records emails for test assertions | `Pragmatic.Email.Testing` |
| `FileTransport` | Writes `.eml` files to disk | `Pragmatic.Email.Testing` |

Only one transport is active at a time. The last registered transport wins. `NullTransport` is the default when no transport is explicitly configured (via `TryAddSingleton`).

### Custom transports

Implement `IEmailTransport` and register via the builder:

```csharp
public sealed class SesTransport : IEmailTransport
{
    public string Name => "SES";

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        // Send via AWS SES SDK
        return EmailResult.Succeeded(message.MessageId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

services.AddPragmaticEmail(email => email.UseTransport<SesTransport>());
```

## Middleware pipeline

`IEmailMiddleware` provides an ordered chain-of-responsibility pipeline. Each middleware can transform the message, add headers, or short-circuit the pipeline:

```csharp
public interface IEmailMiddleware
{
    int Order => 0;

    Task<EmailMessage> ProcessAsync(
        EmailMessage message,
        Func<EmailMessage, Task<EmailMessage>> next,
        CancellationToken ct);
}
```

The `EmailPipeline` sorts middleware by `Order` (ascending) and chains them. Each middleware calls `next(message)` to pass to the next step, or returns directly to short-circuit.

### Execution flow

```
EmailSender.SendAsync(message)
  -> EmailPipeline.ExecuteAsync(message)
       -> Middleware[0].ProcessAsync (Order=10, e.g. BrandingMiddleware)
            -> Middleware[1].ProcessAsync (Order=50, SmimeMiddleware)
                 -> Middleware[2].ProcessAsync (Order=100, DkimMiddleware)
                      -> returns processed message
  -> IEmailTransport.SendAsync(processedMessage)
  -> EmailResult
```

Since `EmailMessage` is an immutable `record`, middleware creates copies using `with {}`:

```csharp
var enriched = message with
{
    Headers = new Dictionary<string, string>(message.Headers)
    {
        ["X-Campaign-Id"] = campaignId,
    },
};
return next(enriched);
```

### Built-in middleware order

| Order | Middleware | Purpose |
|-------|-----------|---------|
| -1000 | `DefaultFromMiddleware` | Applies `EmailOptions.DefaultFrom` when the message has no sender |
| 50 | `SmimeMiddleware` | Wraps the content in a `multipart/signed` |
| 100 | `DkimMiddleware` | Adds the DKIM-Signature header |

S/MIME runs **before** DKIM because it rewrites the body, and DKIM must sign the final message.

Use `Order < 50` for content transformations (branding, tracking pixels): anything that changes the
message after S/MIME has run invalidates both signatures.

## MIME generation

`MimeWriter` is a public static class that generates RFC 2045/2046 compliant MIME from `EmailMessage` —
public because a custom `IEmailTransport` needs to produce the wire format, and `DkimSigner` signs a
rendered message. It handles the full multipart hierarchy:

```
multipart/mixed
  ├── multipart/related
  │     ├── multipart/alternative
  │     │     ├── text/plain
  │     │     └── text/html
  │     └── inline image (Content-ID)
  └── attachment (base64)
```

Key behaviors:

- **Multipart selection**: The writer picks the right multipart structure automatically based on what the message contains (text only, HTML only, both, attachments, inline images).
- **RFC 2047 B-encoding**: Non-ASCII header values are encoded as `=?utf-8?B?...?=`.
- **Base64 line wrapping**: Binary attachments are encoded at 76-character line width per RFC 2045.
- **Deterministic boundaries**: derived from `SHA-256(MessageId | tag)`, so rendering the same message
  twice yields identical bytes. This is a correctness requirement, not an optimisation: a message is
  rendered once by each signing middleware and once by the transport, and a random boundary per call
  would mean every signature covered a document that was never sent.
- **Inline images without an HTML body** are emitted as ordinary attachments rather than dropped.
- **Line endings** are normalised to CRLF. SMTP dot-stuffing is *not* done here — it is a transport
  encoding (RFC 5321 §4.5.2) applied by `SmtpDotStuffing` over the whole DATA block, including its
  first line, and must not be part of what gets signed.

### Transport security

`UseSsl` (default true) means the session must be encrypted. Two ways to get there:

| Mode | When | How |
|------|------|-----|
| STARTTLS | default, typically port 587 | plaintext greeting, then `STARTTLS` upgrades the socket |
| Implicit TLS (SMTPS) | automatic on port 465, or `UseImplicitTls = true` | handshake before the greeting |

If encryption cannot be established the connection is refused rather than continuing in cleartext —
credentials must never travel over a plain socket.

`TimeoutSeconds` applies to connect and to every read and write, with one deadline per response rather
than per read: a server dribbling bytes cannot keep resetting it.

## Connection pooling

`SmtpConnectionPool` manages a bounded pool of `SmtpConnection` instances using `Channel<SmtpConnection>`:

1. **Acquire**: Try to read from the channel. If available and usable, return it. Otherwise, create a new connection (bounded by `SemaphoreSlim` at `MaxConnections`).
2. **Usability check**: A connection is usable if it is still connected, hasn't exceeded `MaxMessagesPerConnection`, and hasn't been idle longer than `IdleTimeoutSeconds`.
3. **Release**: If usable, write back to the channel. If the channel is full or the connection is stale, dispose it and release the semaphore.

### Connection lifecycle

```
New connection:
  TCP connect -> Read 220 greeting -> EHLO -> Parse capabilities
  -> STARTTLS (if UseSsl && server supports it)
  -> Re-EHLO (after TLS upgrade, capabilities may change)
  -> AUTH (PLAIN / LOGIN / XOAUTH2, auto-detected)

Per message:
  MAIL FROM:<sender> -> RCPT TO:<recipient>... -> DATA -> MIME body + "\r\n." -> 250 OK

End of life (MaxMessagesPerConnection reached or idle timeout):
  QUIT -> TCP close
```

### SMTP authentication

`SmtpAuthMethod.Auto` selects the method based on configured credentials:

| Credentials present | Method selected |
|---------------------|-----------------|
| `OAuth2Token` set | `XOAUTH2` |
| `Username` + `Password` set | `PLAIN` |
| Neither set | No authentication |

Explicit override via `SmtpAuthMethod.Plain`, `SmtpAuthMethod.Login`, or `SmtpAuthMethod.XOAuth2`.

## Security

### DKIM signing (RFC 6376)

`DkimSigner` generates DKIM-Signature headers using RSA-SHA256:

`Sign` takes the **rendered MIME message** and signs it as it will appear on the wire:

1. Split it at the blank line; compute the body hash (`bh=`) over the body only, relaxed-canonicalized.
2. Read the header values back **verbatim** from the rendered message — already RFC 2047 encoded where
   needed — instead of reconstructing them from `EmailMessage`. Reconstruction breaks any message with
   an accented subject: the signature would cover `Città` while the wire carries `=?utf-8?B?...?=`.
3. Declare in `h=` only the headers actually present, out of From, To, Cc, Subject, Date, Message-ID,
   MIME-Version, Content-Type and Content-Transfer-Encoding — the content headers are included so the
   signature also pins the structure of the message.
4. Canonicalize those headers plus the DKIM-Signature template (with an empty `b=`) and sign with RSA.
5. Append the base64 signature as `b=`.

Canonicalization follows RFC 6376 section 3.4:

- **Relaxed header**: lowercase name, unfold continuation lines, compress whitespace to single space, trim.
- **Relaxed body**: strip trailing whitespace per line, compress whitespace, remove trailing empty lines.

### S/MIME signing

`SmimeMiddleware` produces a `multipart/signed` per RFC 8551 §3.4.3 — the structure every mail client
understands:

```
multipart/signed; protocol="application/pkcs7-signature"; micalg=sha-256
  ├── the original content, in the clear (readable by clients without S/MIME)
  └── application/pkcs7-signature (smime.p7s) — detached CMS signature
```

1. Render the content part (content headers, blank line, body).
2. Sign exactly what a receiving client extracts as that part. Per RFC 2046 §5.1.1 the CRLF preceding a
   boundary belongs to the delimiter, not to the part, so it is excluded from the signed bytes.
3. Sign with `CmsSigner` using the configured `X509Certificate2`, digest pinned to SHA-256 to match the
   advertised `micalg`.
4. Embed the base64 signature as the second part.

The signing certificate can optionally include the full chain (`IncludeCertificate = true`) for recipients to validate the signature.
