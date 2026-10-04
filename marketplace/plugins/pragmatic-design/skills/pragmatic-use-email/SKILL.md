---
name: pragmatic-use-email
description: Use when the app sends mail directly, configures SMTP, signs with DKIM or S/MIME, or asserts on sent mail (Pragmatic.Email). Bodies are pragmatic-use-documents; routing across channels is pragmatic-use-notifications.
---

# Pragmatic Use Email

**Covers:** Send e-mail with Pragmatic.Email: message builder, pooled SMTP with STARTTLS and AUTH, DKIM and S/MIME signing, middleware, in-memory and file transports for tests.

`Pragmatic.Email` sends mail over SMTP with no external dependencies: it builds the MIME itself, pools
connections, and exposes a middleware pipeline for cross-cutting concerns.

Sending *one* e-mail? Use this. Routing a notification across e-mail/webhook with user preferences and
delivery tracking? Use `pragmatic-use-notifications`, which delegates to this library.

## Packages

```xml
<PackageReference Include="Pragmatic.Email" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Email.Testing" Version="1.0.0-alpha.1" />  <!-- test projects -->
```

## Core pattern

Build the message with the builder, send it through the injected `IEmailSender`:

```csharp
var message = new EmailMessageBuilder()
    .From("booking@hotel.com", "Hotel Booking")
    .To(guest.Email, guest.FullName)
    .Cc("desk@hotel.com")
    .ReplyTo("noreply@hotel.com")
    .Subject("Reservation confirmed")
    .TextBody("Your reservation is confirmed.")
    .HtmlBody("<h1>Your reservation is confirmed.</h1>")   // a real body comes from a template; see below
    .Attach("invoice.pdf", pdfBytes, "application/pdf")
    .Build();

var result = await email.SendAsync(message, ct);
if (!result.Success)
    logger.LogWarning("Mail failed: {Error}", result.ErrorMessage);
```

The builder validates each address as you add it (rejecting control characters, which is what closes
header-injection), so a bad address throws where you wrote it. `Build()` requires From, at least one
To, a Subject, and one of TextBody/HtmlBody.

Supplying both bodies produces `multipart/alternative`. Inline images:

```csharp
builder.HtmlBody("""<img src="cid:logo">""")
       .InlineImage("logo", logoBytes, "image/png");
```

An inline image only works alongside an HTML body that references it; without one it is delivered as an
ordinary attachment.

## The body: a template, not a string

`HtmlBody("<h1>…</h1>")` is fine for a test and wrong for a mail a user reads: the wording belongs in
a `.pdxemail` template, where it is translated, reviewed and changed without a build. Composing it is
`pragmatic-use-documents` (`IPdxTemplates`, in `Pragmatic.Documents.Markup`, for the mail's **content**);
this module only **sends** what it produces:

```csharp
var mail = await templates.EmailAsync("overdue-reminder.pdxemail", customer.Language, data, ct);

var message = new EmailMessageBuilder()
    .From(sender.Address, sender.Name)
    .To(recipient.Email, recipient.Name)
    .Subject(mail.Subject)                   // the template owns it too
    .HtmlBody(mail.Html)
    .TextBody(mail.Text)                     // from the same model, never a second copy of the words
    .Attach($"{number}.pdf", storedPdf, "application/pdf")   // the bytes you stored, not a second render
    .Build();
```

⚠️ Always send the text part: some clients show an HTML-only mail as an attachment.

⚠️ The language is the **recipient's**, passed to `EmailAsync`: a mail is usually composed from a
handler or a job, with no request culture anywhere, and the ambient one is whatever the thread last held.

## Host wiring

```csharp
app.UseEmail(email =>
{
    email.UseSmtp(smtp =>
    {
        smtp.Host = cfg["Smtp:Host"]!;
        smtp.Port = 587;
        smtp.UseSsl = true;         // encryption required; refuses to continue in cleartext
        // smtp.UseImplicitTls = true;  // SMTPS, inferred automatically when Port is 465
        smtp.Username = cfg["Smtp:User"];
        smtp.Password = cfg["Smtp:Password"];
        smtp.MaxConnections = 5;
    });

    email.Configure(o => o.DefaultFrom = new EmailAddress("noreply@hotel.com", "Hotel"));
});
```

`DefaultFrom` fills in the sender for messages built without one. With neither, the send fails with an
explanatory error rather than emitting an empty `From`.

Transport selection is last-call-wins: `UseSmtp`, `UseNullTransport`, `UseTransport<T>`.

## Signing

```csharp
app.UseEmail(email =>
{
    email.EnableDkim(d =>
    {
        d.Domain = "hotel.com";
        d.Selector = "pragmatic";           // DNS: pragmatic._domainkey.hotel.com
        d.PrivateKeyPem = cfg["Dkim:PrivateKey"]!;   // from a secret store, never appsettings
    });

    email.EnableSmime(s => s.SigningCertificate = certificate);  // needs the private key
});
```

DKIM signs the message as it goes on the wire and covers From/To/Cc/Subject/Date/Message-ID plus the
content headers. S/MIME produces a real `multipart/signed` (RFC 8551), so ordinary clients verify it and
clients without S/MIME still read the message.

Ordering matters and is handled for you: S/MIME (Order 50) rewrites the body, DKIM (Order 100) then
signs the final message. **Any middleware that changes content must run before Order 50**, otherwise it
invalidates both signatures.

## Middleware

```csharp
internal sealed class SandboxMiddleware(IHostEnvironment env) : IEmailMiddleware
{
    public int Order => -100;   // before signing

    public Task<EmailMessage> ProcessAsync(
        EmailMessage message, Func<EmailMessage, Task<EmailMessage>> next, CancellationToken ct)
    {
        if (!env.IsProduction())
            message = message with { To = [new EmailAddress("qa@hotel.com")] };

        return next(message);
    }
}

app.UseEmail(e => e.AddMiddleware<SandboxMiddleware>());
```

`EmailMessage` is an immutable record: transform it with `with { }` and pass it to `next`. Returning
without calling `next` short-circuits the send.

## Testing

```csharp
var transport = services.AddEmailTestHarness();   // replaces IEmailTransport with InMemoryTransport

await sender.SendAsync(message);

transport.HasSentTo("guest@example.com").Should().BeTrue();
transport.Sent[0].Message.Subject.Should().Be("Reservation confirmed");
```

`FileTransport` (in `Pragmatic.Email.Testing`, like the harness) writes `.eml` files to a folder for eyeballing during development;
`NullTransport` discards everything and is the default when no transport is configured.

## Custom transport

`IEmailTransport` is the seam for provider APIs (SES, SendGrid). Render the wire format with
`MimeWriter.Write(message)` when the provider wants raw MIME:

```csharp
public sealed class SesTransport(IAmazonSimpleEmailService ses) : IEmailTransport
{
    public string Name => "SES";

    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var raw = MimeWriter.Write(message);
        // hand `raw` to the provider …
        return EmailResult.Succeeded(message.MessageId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

app.UseEmail(e => e.UseTransport<SesTransport>());
```

## TLS, timeouts, headers

- TLS defaults to STARTTLS and switches to **implicit TLS (SMTPS)** automatically on port 465. Force
  either mode with `UseImplicitTls = true/false`. If `UseSsl` is on and encryption cannot be
  established, the connection is refused rather than falling back to cleartext.
- `TimeoutSeconds` (default 30) bounds connect **and** every read/write, so a server that accepts the
  connection and goes silent cannot stall the send.
- Headers the message owns (From, To, Cc, Bcc, Subject, Date, Message-ID, Content-*) cannot be set
  through `Headers`; use the corresponding property. Custom `X-…` headers are free.

## Limits to know

- Bodies are sent `8bit`; quoted-printable is not implemented.
- Never put SMTP passwords or the DKIM private key in `appsettings.json`; use user-secrets, environment
  variables or a secrets manager.

> Licensing: `Pragmatic.Email` is PolyForm Small Business (free for small businesses).

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Invoicing example application (code
that compiles and that `Invoicing.IntegrationTests` exercises) and kept identical to it by the gate: the
message built from a template with the stored PDF attached, the send and what is recorded only after it,
the host's `UseEmail`, and the test harness reading the mailbox.

DKIM and S/MIME signing, mail middleware and a custom transport are used by no tested application yet, so
there is no example of them here: the sections above are the reference.
