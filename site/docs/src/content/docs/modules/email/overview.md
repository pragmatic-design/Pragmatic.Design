---
title: "Pragmatic.Email"
description: "Email for .NET without a third-party mail library — SMTP with connection pooling, DKIM / S/MIME"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Email/README.md
sidebar:
  order: 0
  label: Overview
---
Email for .NET without a third-party mail library — SMTP with connection pooling, DKIM / S/MIME
signing, a middleware pipeline, and first-class testing support.

## The Problem

Sending email in .NET usually means pulling in MailKit (200K+ lines, dozens of transitive
dependencies) for what should be straightforward — and you still hand-wire the message, with no
connection pooling and no pipeline.

```csharp
// Typical MailKit setup — new TCP connection every time, no pipeline
using var client = new SmtpClient();
await client.ConnectAsync("smtp.hotel.com", 587, SecureSocketOptions.StartTls);
await client.AuthenticateAsync("user", "pass");
await client.SendAsync(message);
await client.DisconnectAsync(true);
```

## The Solution

A small email library — it depends only on `Microsoft.Extensions.*` abstractions and
`System.Security.Cryptography.Pkcs` — with a fluent message API, pooled SMTP connections, optional
DKIM/S-MIME signing, a middleware pipeline and in-memory/file transports for tests. The shipped
middleware are `DefaultFromMiddleware`, `DkimMiddleware` and `SmimeMiddleware`; add your own with
`EmailBuilder.AddMiddleware<T>()`.

```csharp
var message = new EmailMessageBuilder()
    .From("booking@hotel.com", "Hotel Booking")
    .To(guest.Email)
    .Subject("Reservation Confirmed")
    .HtmlBody("<h1>Confirmed</h1>...")
    .TextBody("Confirmed ...")
    .Attach("invoice.pdf", pdfBytes, "application/pdf")
    .Build();

EmailResult sent = await emailSender.SendAsync(message);   // IEmailSender
```

**This module sends; it does not write the body.** A mail a person reads comes from a `.pdxemail`
template resolved against data — `Pragmatic.Email.Templates`, `Pragmatic.Email.Model` and
`Pragmatic.Documents.Email`, all part of [Pragmatic.Documents](/modules/documents/overview/) —
and its HTML and text go into `HtmlBody` / `TextBody` here. See
[HTML email rendering](/modules/documents/email-rendering/).

Connections are pooled across sends; configure the transport once in the host. The builder validates
addresses as you add them, so a malformed one fails where you wrote it rather than at send time.

## Installation

```bash
dotnet add package Pragmatic.Email
dotnet add package Pragmatic.Email.Testing   # InMemoryTransport, FileTransport — test projects only
```

## Status

SMTP transport, pooling, DKIM and S/MIME signing, the middleware pipeline, and test support are
functional within 1.0.0-alpha. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

TLS is negotiated with STARTTLS by default and switches to implicit TLS (SMTPS) automatically on port
465 — set `UseImplicitTls` to force either mode. `TimeoutSeconds` bounds connect, reads and writes.

Known limit: bodies are sent as `8bit`; quoted-printable is not implemented.

| [Concepts](/modules/email/concepts/) | Message model, transport, pooling, the middleware pipeline |
| [Getting Started](/modules/email/getting-started/) | Send your first email, configure SMTP, test it |
| [Common Mistakes](/modules/email/common-mistakes/) | The most frequent email pitfalls |
| [Troubleshooting](/modules/email/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/email/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Email is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
