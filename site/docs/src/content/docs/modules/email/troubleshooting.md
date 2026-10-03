---
title: "Troubleshooting"
description: "Pragmatic.Email uses structured logging via `[LoggerMessage]` and OpenTelemetry metrics."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Email/docs/troubleshooting.md
sidebar:
  order: 4
---
## Diagnostics

Pragmatic.Email uses structured logging via `[LoggerMessage]` and OpenTelemetry metrics.

### Log messages

| Level | Message | Meaning |
|-------|---------|---------|
| Information | `Sending email: {Subject} to {RecipientCount} recipient(s)` | Email entered the pipeline |
| Information | `Email sent: {MessageId}` | Transport returned success |
| Warning | `Email failed: {MessageId} — {Error}` | Transport returned failure |
| Debug | `SMTP connection acquired (messages sent: {MessagesSent})` | Connection taken from pool |
| Information | `Email sent via SMTP: {MessageId} to {Address}` | SMTP DATA accepted |
| Warning | `Email failed via SMTP: {MessageId} — {Error}` | SMTP error response |
| Error | `Email exception via SMTP: {MessageId}` | Unhandled exception during send |

### OpenTelemetry metrics

Subscribe to the `Pragmatic.Email` meter:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Pragmatic.Email"));
```

Available counters and histograms:

| Metric name | Type | Unit |
|-------------|------|------|
| `pragmatic.email.sent` | Counter | emails |
| `pragmatic.email.failed` | Counter | emails |
| `pragmatic.email.send.duration` | Histogram | ms |
| `pragmatic.email.connections.created` | Counter | - |
| `pragmatic.email.connections.recycled` | Counter | - |

## FAQ

### Emails are "sent" but nobody receives them

**Check which transport is active.** If you didn't call `UseSmtp()`, the default `NullTransport` is registered. It always returns success without sending anything.

```csharp
// This registers NullTransport by default: no actual email delivery
services.AddPragmaticEmail();
```

Fix: explicitly configure SMTP.

```csharp
services.AddPragmaticEmail(email => email.UseSmtp(smtp => { /* ... */ }));
```

### AUTH PLAIN failed / AUTH LOGIN failed

The SMTP server rejected your credentials.

1. Verify `Username` and `Password` are correct.
2. Check if the server requires app-specific passwords (Gmail, Outlook 365).
3. If the server requires LOGIN instead of PLAIN, set `AuthMethod = SmtpAuthMethod.Login`.
4. For OAuth2 (Google Workspace, Microsoft 365), use `OAuth2Token` instead of `Password`.

```csharp
smtp.AuthMethod = SmtpAuthMethod.Login; // Force LOGIN instead of PLAIN
```

### AUTH XOAUTH2 failed

1. Ensure the OAuth2 token is not expired. Tokens typically expire after 1 hour.
2. Verify the token has the correct scope (e.g., `https://mail.google.com/` for Gmail).
3. Set both `Username` (the email address) and `OAuth2Token`:

```csharp
smtp.Username = "booking@hotel.com";
smtp.OAuth2Token = accessToken; // Fresh bearer token
```

### STARTTLS not upgrading

If `UseSsl = true` (default) but the server doesn't advertise STARTTLS in its EHLO capabilities, the connection continues without TLS. This is by design: the library upgrades opportunistically.

If you need to enforce TLS:
- Use port 465 (implicit TLS) instead of 587 (STARTTLS).
- Check your SMTP server configuration to ensure STARTTLS is enabled.

### Connection timeouts

Default timeout is 30 seconds. For slow networks or overloaded SMTP servers:

```csharp
smtp.TimeoutSeconds = 60; // Increase to 60 seconds
```

If timeouts persist, check:
- Firewall rules (port 587 or 465 must be open).
- DNS resolution for the SMTP hostname.
- Whether the SMTP server is actually accepting connections.

### Pool exhausted: sends are slow

If you see long waits before `SMTP connection acquired`, the pool is saturated.

1. Check `MaxConnections`: default is 5. Increase if your SMTP server allows it.
2. Check `MaxMessagesPerConnection`: default is 100. If connections are recycled too often, increase it.
3. Check `IdleTimeoutSeconds`: default is 30. If connections expire between bursts, increase it.

```csharp
smtp.MaxConnections = 10;
smtp.MaxMessagesPerConnection = 200;
smtp.IdleTimeoutSeconds = 120;
```

### DKIM signature is invalid

Recipients' mail servers report DKIM failure (`dkim=fail` in headers).

1. **DNS record**: Verify the TXT record at `{selector}._domainkey.{domain}` contains the correct public key.
2. **Key mismatch**: The PEM private key must match the DNS public key.
3. **Domain mismatch**: `DkimOptions.Domain` must match the `From` address domain.
4. **Middleware order**: Content-modifying middleware running after DKIM (Order > 100) invalidates the signature. Move it to Order < 100.

Test your DKIM setup:

```bash
# Check DNS record
dig TXT pragmatic._domainkey.hotel.com

# Send a test email to a Gmail address and check the "Show original" headers
```

### RCPT TO rejected

The SMTP server rejected a recipient address. Common causes:

- Typo in the email address.
- The recipient's mailbox doesn't exist.
- The server is rejecting relay attempts (you're not authorized to send to external domains).

The error message includes the specific SMTP response: `RCPT TO <user@example.com> rejected: 550 5.1.1 User unknown`.

### .eml files from FileTransport won't open

FileTransport generates standard `.eml` (MIME) files. They open with:
- **Thunderbird**: File > Open Saved Message.
- **Outlook**: Double-click the file.
- **macOS Mail**: Double-click or drag into Mail.
- **VS Code**: With an EML viewer extension.

If the file appears corrupted, check the MIME structure by opening it in a text editor.

### InMemoryTransport shows zero emails in tests

1. Ensure `AddEmailTestHarness()` is called **after** `AddPragmaticEmail()`: it replaces the transport registration.
2. Verify you're resolving `IEmailSender` from the same `ServiceProvider` that has the test harness.
3. Check that the email `Build()` call doesn't throw (missing From, To, Subject, or body).

```csharp
var services = new ServiceCollection();
services.AddPragmaticEmail();                    // 1. Register email
var transport = services.AddEmailTestHarness();   // 2. Replace transport
var sp = services.BuildServiceProvider();
var sender = sp.GetRequiredService<IEmailSender>(); // 3. Resolve from same SP
```

### S/MIME signing fails with "The certificate does not have a private key"

`SmimeOptions.SigningCertificate` must be an `X509Certificate2` with a private key. Loading from `.cer` or `.crt` files gives you only the public certificate.

Fix: Load from `.pfx` or `.p12`:

```csharp
.EnableSmime(smime =>
{
    smime.SigningCertificate = new X509Certificate2("cert.pfx", "password");
})
```
