# Getting Started

Step-by-step guide to sending emails with Pragmatic.Email.

## 1. Install the package

```bash
dotnet add package Pragmatic.Email
```

## 2. Register the email service

### Standalone (without Pragmatic.Design)

```csharp
builder.Services.AddPragmaticEmail(email => email
    .UseSmtp(smtp =>
    {
        smtp.Host = "smtp.hotel.com";
        smtp.Port = 587;
        smtp.Username = "booking@hotel.com";
        smtp.Password = "secret";
    }));
```

### With Pragmatic.Design

```csharp
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UseEmail(email => email
        .UseSmtp(smtp =>
        {
            smtp.Host = "smtp.hotel.com";
            smtp.Port = 587;
            smtp.Username = "booking@hotel.com";
            smtp.Password = "secret";
        }));
});
```

## 3. Send your first email

Inject `IEmailSender` and build a message:

```csharp
public class BookingConfirmationService(IEmailSender emailSender)
{
    public async Task SendConfirmation(string guestEmail, string guestName, string reservationId)
    {
        var email = new EmailMessageBuilder()
            .From("booking@hotel.com", "Grand Hotel")
            .To(guestEmail, guestName)
            .Subject($"Booking #{reservationId} Confirmed")
            .TextBody($"""
                Dear {guestName},

                Your booking #{reservationId} has been confirmed.
                We look forward to welcoming you.

                Grand Hotel
                """)
            .Build();

        var result = await emailSender.SendAsync(email);

        if (!result.Success)
            throw new InvalidOperationException($"Failed to send confirmation: {result.ErrorMessage}");
    }
}
```

`EmailResult` tells you whether the SMTP transaction succeeded. On success, `MessageId` contains the generated ID. On failure, `ErrorMessage` contains the SMTP error or exception message.

## 4. Send HTML with text fallback

Always provide both for maximum compatibility:

```csharp
var email = new EmailMessageBuilder()
    .From("billing@hotel.com", "Grand Hotel Billing")
    .To(invoice.CustomerEmail)
    .Subject($"Invoice #{invoice.Number}")
    .TextBody($"Invoice #{invoice.Number} — Total: {invoice.Total:C}")
    .HtmlBody($"""
        <html>
        <body>
            <h1>Invoice #{invoice.Number}</h1>
            <table>
                <tr><td>Guest</td><td>{invoice.CustomerName}</td></tr>
                <tr><td>Total</td><td>{invoice.Total:C}</td></tr>
            </table>
        </body>
        </html>
        """)
    .Build();
```

When both `TextBody` and `HtmlBody` are set, the MIME writer generates a `multipart/alternative` message. Email clients pick whichever version they prefer.

## 5. Add attachments

```csharp
var pdfBytes = await GenerateInvoicePdf(invoice);
var csvBytes = await GenerateLineItemsCsv(invoice);

var email = new EmailMessageBuilder()
    .From("billing@hotel.com", "Grand Hotel Billing")
    .To(invoice.CustomerEmail)
    .Subject($"Invoice #{invoice.Number}")
    .HtmlBody(invoiceHtml)
    .TextBody(invoiceText)
    .Attach("invoice.pdf", pdfBytes, "application/pdf")
    .Attach("line-items.csv", csvBytes, "text/csv")
    .Build();
```

Attachments are base64-encoded in the MIME body. The `ContentType` parameter is the MIME type string.

## 6. Inline images

For images embedded in HTML (not as file attachments):

```csharp
var logoBytes = await File.ReadAllBytesAsync("wwwroot/images/logo.png");

var email = new EmailMessageBuilder()
    .From("marketing@hotel.com")
    .To("guest@example.com")
    .Subject("Special Offer")
    .HtmlBody("""
        <html>
        <body>
            <img src="cid:hotel-logo" alt="Grand Hotel" />
            <h1>20% Off Your Next Stay</h1>
        </body>
        </html>
        """)
    .InlineImage("hotel-logo", logoBytes, "image/png")
    .Build();
```

The `InlineImage` method sets `IsInline = true` and `ContentId` on the attachment. Reference it in HTML via `cid:{contentId}`.

## 7. Enable DKIM signing

DKIM prevents email spoofing and improves deliverability. Configure it once at startup:

```csharp
services.AddPragmaticEmail(email => email
    .UseSmtp(smtp =>
    {
        smtp.Host = "smtp.hotel.com";
        smtp.Username = "noreply@hotel.com";
        smtp.Password = "secret";
    })
    .EnableDkim(dkim =>
    {
        dkim.Domain = "hotel.com";
        dkim.Selector = "pragmatic";
        dkim.PrivateKeyPem = File.ReadAllText("/etc/dkim/hotel.com.pem");
    }));
```

All emails are automatically signed. No changes to sending code.

**DNS setup**: Create a TXT record at `pragmatic._domainkey.hotel.com` with your DKIM public key.

## 8. Add custom middleware

Create middleware to enrich or transform every outgoing email:

```csharp
public class TrackingMiddleware : IEmailMiddleware
{
    public int Order => 20;

    public Task<EmailMessage> ProcessAsync(
        EmailMessage message,
        Func<EmailMessage, Task<EmailMessage>> next,
        CancellationToken ct)
    {
        var headers = new Dictionary<string, string>(message.Headers)
        {
            ["X-Tracking-Id"] = Guid.CreateVersion7().ToString(),
        };

        return next(message with { Headers = headers });
    }
}
```

Register it:

```csharp
services.AddPragmaticEmail(email => email
    .UseSmtp(/* ... */)
    .AddMiddleware<TrackingMiddleware>());
```

## 9. Testing with InMemoryTransport

Install the testing package in your test project:

```bash
dotnet add package Pragmatic.Email.Testing
```

Use `AddEmailTestHarness()` to replace the transport:

```csharp
public class BookingEmailTests
{
    [Fact]
    public async Task ConfirmReservation_SendsEmailToGuest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPragmaticEmail();
        var transport = services.AddEmailTestHarness();

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<IEmailSender>();

        // Act
        var email = new EmailMessageBuilder()
            .From("booking@hotel.com")
            .To("guest@example.com")
            .Subject("Booking Confirmed")
            .TextBody("Your booking is confirmed.")
            .Build();
        await sender.SendAsync(email);

        // Assert
        transport.HasSentTo("guest@example.com").Should().BeTrue();
        transport.HasSentWithSubject("Booking Confirmed").Should().BeTrue();

        var sent = transport.Sent;
        sent.Should().HaveCount(1);
        sent[0].Message.TextBody.Should().Contain("confirmed");
    }
}
```

### InMemoryTransport assertion methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Sent` | `IReadOnlyList<SentEmail>` | All recorded emails |
| `HasSentTo(address)` | `bool` | Any email sent to address |
| `HasSentWithSubject(subject)` | `bool` | Any email with exact subject |
| `HasSent(predicate)` | `bool` | Any email matching predicate |
| `SentWhere(predicate)` | `IReadOnlyList<SentEmail>` | All emails matching predicate |
| `Reset()` | `void` | Clear all recorded emails |

### FileTransport for development

Write `.eml` files to inspect in an email client:

```csharp
services.AddPragmaticEmail(email => email
    .UseNullTransport()); // Or don't configure any transport

// Override with FileTransport for dev
services.AddSingleton<IEmailTransport>(new FileTransport("./emails"));
```

Generated files: `20260331-143022-019a3e5b7c8d9e0f.eml`. Open with Thunderbird or any `.eml` viewer.

## 10. CC, BCC, and Reply-To

```csharp
var email = new EmailMessageBuilder()
    .From("booking@hotel.com", "Grand Hotel")
    .To("guest@example.com", "John Smith")
    .Cc("concierge@hotel.com", "Concierge")
    .Bcc("audit@hotel.com")
    .ReplyTo("support@hotel.com", "Guest Support")
    .Subject("Reservation Details")
    .TextBody("...")
    .Build();
```

Multiple recipients: call `.To()`, `.Cc()`, or `.Bcc()` multiple times.

## 11. Custom headers

```csharp
var email = new EmailMessageBuilder()
    .From("noreply@hotel.com")
    .To("guest@example.com")
    .Subject("Password Reset")
    .TextBody("Click the link to reset your password.")
    .Header("X-Priority", "1")
    .Header("X-Mailer", "Pragmatic.Email")
    .Build();
```

## 12. Using EmailMessage directly

The builder is optional. You can construct `EmailMessage` directly:

```csharp
var email = new EmailMessage
{
    From = new EmailAddress("booking@hotel.com", "Grand Hotel"),
    To = [new EmailAddress("guest@example.com")],
    Subject = "Direct Construction",
    TextBody = "This works too.",
};
```

`EmailAddress` also supports implicit conversion from `string`:

```csharp
EmailAddress address = "guest@example.com"; // Works
```
