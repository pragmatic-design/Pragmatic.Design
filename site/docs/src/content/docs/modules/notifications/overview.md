---
title: "Pragmatic.Notifications"
description: "A unified notification pipeline for .NET — one API to send email, webhook, SMS, push, in-app, and Slack"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Notifications/README.md
sidebar:
  order: 0
  label: Overview
---
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

**Functional** within 1.0.0-alpha — the pipeline, channel routing, user preferences, delivery tracking,
and background delivery. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

**Channels provided today: e-mail (SMTP), webhook, Slack and SMS (Twilio).** `NotificationChannel`
also declares `Push` and `InApp`; those are extension points, not implementations — selecting one
without registering a provider for it makes the send fail with an explanatory error. Implement
`INotificationChannel` and register it with `AddChannel<T>()` to add your own.

**Addressing a user id, role or tenant requires a recipient resolver.** The built-in resolver only
understands direct addresses (`Direct`, `ToWebhook`, phone). Turning `NotificationRecipient.User(id)`
into an address means telling the library where users live — see
[Getting Started](/modules/notifications/getting-started/#resolving-recipients).

| [Concepts](/modules/notifications/concepts/) | The pipeline, channels, routing, preferences, delivery tracking |
| [Getting Started](/modules/notifications/getting-started/) | Send your first notification, configure a channel |
| [Common Mistakes](/modules/notifications/common-mistakes/) | The most frequent notification pitfalls |
| [Troubleshooting](/modules/notifications/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/notifications/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Notifications is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
