# Pragmatic.Notifications

A unified notification pipeline for .NET — one API to send email, webhook, SMS, push, in-app, and Slack
notifications, with channel routing, user preferences, delivery tracking, and background processing.

## The Problem

Every notification use case grows its own ad-hoc delivery code — a fresh SMTP client here, an HTTP
webhook there, a Twilio call elsewhere — each with its own setup, error handling, retry, and tracking.
Add a channel and every call site must change; enforcing user preferences (muted categories,
do-not-disturb) consistently becomes impossible.

```csharp
await smtpClient.SendMailAsync(new MailMessage("noreply@hotel.com", guestEmail) { /* ... */ });
await httpClient.PostAsync("https://hooks.slack.com/...", slackJson);
await MessageResource.CreateAsync(to: guestPhone, from: "+1555000", body: "...");
```

## The Solution

Send a notification through one pipeline; channel routing, user preferences, delivery tracking, and
background processing are handled for you.

```csharp
await notifier.SendAsync(new NotificationRequest
{
    Audience  = NotificationAudience.EndUser,
    Recipient = NotificationRecipient.Direct(guest.Email),
    Content   = new NotificationContent
    {
        Subject = "Reservation confirmed",
        Body    = "Your reservation has been confirmed.",
    },
    Category = "transactional",
});
```

Configure channels once in the host (`app.UseNotifications(n => n.AddSmtp(...))`); add a new channel
without touching call sites; user preferences and do-not-disturb are enforced centrally.

Use `EnqueueAsync` instead of `SendAsync` to hand the notification to the background worker and return
immediately with a tracking id.

## Installation

```bash
dotnet add package Pragmatic.Notifications
dotnet add package Pragmatic.Notifications.Webhook   # optional: webhook channel
dotnet add package Pragmatic.Notifications.Slack     # optional: Slack incoming webhooks
dotnet add package Pragmatic.Notifications.Sms       # optional: SMS via Twilio
dotnet add package Pragmatic.Notifications.EFCore    # optional: database-backed delivery tracking
dotnet add package Pragmatic.Notifications.Testing   # test projects: capture what was sent
```

The SMTP channel ships inside `Pragmatic.Notifications` itself and delegates to `Pragmatic.Email`.

## Status

The pipeline, channel routing, user preferences, delivery tracking and background delivery are
functional within 1.0.0-alpha. See the [roadmap](../docs/ROADMAP.md).

**Channels provided today: e-mail (SMTP), webhook, Slack and SMS (Twilio).** `NotificationChannel`
also declares `Push` and `InApp`; those are extension points, not implementations — selecting one
without registering a provider for it makes the send fail with an explanatory error. Implement
`INotificationChannel` and register it with `AddChannel<T>()` to add your own.

**Addressing a user id, role or tenant requires a recipient resolver.** The built-in resolver only
understands direct addresses (`Direct`, `ToWebhook`, phone). Turning `NotificationRecipient.User(id)`
into an address means telling the library where users live — see
[Getting Started](docs/getting-started.md#resolving-recipients).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | The pipeline, channels, routing, preferences, delivery tracking |
| [Getting Started](docs/getting-started.md) | Send your first notification, configure a channel |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent notification pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Notifications is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
