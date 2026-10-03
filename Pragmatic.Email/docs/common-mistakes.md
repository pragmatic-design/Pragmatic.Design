# Common Mistakes

## 1. Forgetting to set a transport

```csharp
// Problem: no transport configured, NullTransport is the default
services.AddPragmaticEmail();

var result = await emailSender.SendAsync(email);
// result.Success == true but no email was actually sent!
```

When no transport is configured, `NullTransport` is registered as the default (via `TryAddSingleton`). It silently succeeds without sending anything.

**Fix**: Always configure a transport explicitly.

```csharp
services.AddPragmaticEmail(email => email
    .UseSmtp(smtp =>
    {
        smtp.Host = "smtp.example.com";
        smtp.Username = "user";
        smtp.Password = "pass";
    }));
```

## 2. Not providing both TextBody and HtmlBody

```csharp
// Problem: HTML-only email; some clients won't render it
var email = new EmailMessageBuilder()
    .From("noreply@hotel.com")
    .To("guest@example.com")
    .Subject("Confirmation")
    .HtmlBody("<h1>Confirmed!</h1>")
    .Build();
```

Some email clients (especially plain-text clients, accessibility readers, and corporate firewalls) strip HTML. If there is no text fallback, the recipient sees nothing.

**Fix**: Always provide both bodies.

```csharp
var email = new EmailMessageBuilder()
    .From("noreply@hotel.com")
    .To("guest@example.com")
    .Subject("Confirmation")
    .HtmlBody("<h1>Confirmed!</h1>")
    .TextBody("Your booking is confirmed.")
    .Build();
```

## 3. Ignoring EmailResult

```csharp
// Problem: fire-and-forget; failures go unnoticed
await emailSender.SendAsync(email);
```

SMTP can fail for many reasons: authentication errors, rejected recipients, network timeouts. If you ignore the result, you won't know.

**Fix**: Check `result.Success` and handle failures.

```csharp
var result = await emailSender.SendAsync(email);
if (!result.Success)
    logger.LogWarning("Email to {Address} failed: {Error}", guest.Email, result.ErrorMessage);
```

## 4. Creating a new EmailMessageBuilder per field

```csharp
// Problem: not a real issue, but verbose and easy to lose the chain
var builder = new EmailMessageBuilder();
builder.From("noreply@hotel.com");
builder.To("guest@example.com");
// Forgot to call builder.Subject(...), so Build() will throw
var email = builder.Build();
```

The builder validates at `Build()` time. Missing `From`, `To`, `Subject`, or both bodies throws `InvalidOperationException`.

**Fix**: Use the fluent chain; it's harder to skip required fields.

```csharp
var email = new EmailMessageBuilder()
    .From("noreply@hotel.com")
    .To("guest@example.com")
    .Subject("Required")
    .TextBody("Also required")
    .Build();
```

## 5. Putting DKIM private key in source code

```csharp
// Problem: private key hardcoded
.EnableDkim(dkim =>
{
    dkim.Domain = "hotel.com";
    dkim.Selector = "pragmatic";
    dkim.PrivateKeyPem = "-----BEGIN RSA PRIVATE KEY-----\nMIIE...";
})
```

**Fix**: Load from file, environment variable, or secrets manager.

```csharp
.EnableDkim(dkim =>
{
    dkim.Domain = "hotel.com";
    dkim.Selector = "pragmatic";
    dkim.PrivateKeyPem = File.ReadAllText(
        Environment.GetEnvironmentVariable("DKIM_KEY_PATH")!);
})
```

## 6. Setting MaxConnections too high

```csharp
smtp.MaxConnections = 100; // Problem: overwhelms SMTP server
```

Most SMTP servers throttle connections per IP. Exceeding the limit causes connection rejections or bans.

**Fix**: Start with the default (5) and tune based on your SMTP provider's limits. For SES, 10 is typically fine. For shared hosting SMTP, 2-3 is safer.

```csharp
smtp.MaxConnections = 5; // Default, good for most use cases
```

## 7. Not resetting InMemoryTransport between tests

```csharp
// Problem: emails from previous test bleed into the next test
[Fact]
public async Task Test1()
{
    await sender.SendAsync(email1);
    transport.Sent.Should().HaveCount(1); // Passes
}

[Fact]
public async Task Test2()
{
    await sender.SendAsync(email2);
    transport.Sent.Should().HaveCount(1); // FAILS: count is 2!
}
```

If you share the same `InMemoryTransport` instance across tests (e.g., via a shared fixture), emails accumulate.

**Fix**: Call `Reset()` in setup, or use a fresh `ServiceProvider` per test.

```csharp
transport.Reset();
// or
var transport = services.AddEmailTestHarness(); // Fresh instance per test
```

## 8. Using wrong Content-ID format for inline images

```csharp
// Problem: wrapping Content-ID in angle brackets
.InlineImage("<logo>", logoBytes, "image/png")

// Then in HTML:
// <img src="cid:<logo>" />  (double brackets, won't display)
```

The `InlineImage` method sets the Content-ID directly. The MIME writer adds `<>` around it in the header. Don't add them yourself.

**Fix**: Use the bare identifier, and reference it as `cid:{id}` in HTML.

```csharp
.InlineImage("logo", logoBytes, "image/png")

// HTML:
// <img src="cid:logo" />
```

## 9. Middleware ordering: content changes after signing

```csharp
public class FooterMiddleware : IEmailMiddleware
{
    public int Order => 150; // Problem: runs AFTER DKIM (100)
    // ...
}
```

DKIM signs the message content. If middleware modifies the body or signed headers after DKIM runs, the signature becomes invalid and recipients' mail servers will reject or flag the email.

**Fix**: Content-modifying middleware must have `Order < 100` (before DKIM).

```csharp
public int Order => 10; // Before DKIM (100) and S/MIME (200)
```

## 10. Disposing IEmailSender manually

```csharp
// Problem: IEmailSender doesn't implement IDisposable
using var sender = sp.GetRequiredService<IEmailSender>(); // Compile error
```

`IEmailSender` is not disposable. The underlying `IEmailTransport` is `IAsyncDisposable` and is managed by the DI container. The connection pool is cleaned up when the host shuts down.

**Fix**: Let DI manage the lifetime. Just inject `IEmailSender` and use it.
