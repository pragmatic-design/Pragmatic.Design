# Concepts & Architecture

## Overview

Pragmatic.Notifications is a channel-agnostic notification pipeline. The caller constructs a `NotificationRequest` describing *what* to send and *to whom*; the pipeline decides *how* to deliver it.

The design separates three concerns:

1. **Recipient resolution** -- translating logical targets (user ID, role, tenant) into concrete addresses
2. **Channel routing** -- selecting delivery channels based on priority, preferences, and registered providers
3. **Delivery tracking** -- persisting every delivery attempt with lifecycle status

## Pipeline Stages

```
NotificationRequest
       |
  1. Validate       Checks Subject and Body are non-empty
       |
  2. Resolve         IRecipientResolver: logical recipient -> ResolvedRecipient[]
       |
  3. Route           INotificationRouter: priority + preferences -> NotificationChannel flags
       |
  4. Deliver         INotificationChannel.DeliverAsync per channel per recipient
       |
  5. Track           INotificationStore: create record, update status (Sent/Failed)
       |
  NotificationResult
```

### 1. Validate

Minimal validation: `Subject` and `Body` must be non-empty. If validation fails, the pipeline returns `NotificationResult.Failed(...)` immediately. No records are created.

### 2. Resolve

`IRecipientResolver.ResolveAsync` converts a `NotificationRecipient` into one or more `ResolvedRecipient` records. Each resolved recipient has:

- `Address` -- concrete delivery target (email, URL, phone number)
- `Channel` -- the channel inferred from the address type
- `Locale` -- for future localized content
- `TenantId` -- for multi-tenant routing
- `Preferences` -- user notification preferences (if available)

The `DefaultRecipientResolver` handles direct addresses (email, phone, webhook). For user ID, role, or tenant resolution, register a custom `IRecipientResolver` that queries your identity system.

### 3. Route

`INotificationRouter.Route` determines which channels to use for each resolved recipient. The `DefaultNotificationRouter` applies priority-based logic:

| Priority | Strategy |
|----------|----------|
| `Critical` | Combine all registered channels (OR all flags) |
| `High` | Email + Push (if registered), fallback to first registered |
| `Normal` | Recipient's resolved channel, or email fallback |
| `Low` | Email only (digest candidate), or first registered |

Before routing, preferences are checked:
- If `Preferences.Enabled` is `false`, returns `NotificationChannel.None` (skip)
- If the `Category` is in `Preferences.MutedCategories`, returns `NotificationChannel.None`

The `ChannelOverride` property on `NotificationRequest` bypasses routing entirely.

### 4. Deliver

For each resolved recipient, the pipeline iterates over individual channel flags and calls `INotificationChannelFactory.GetChannel(flag, tenantId)`. If a channel provider is registered, it calls `INotificationChannel.DeliverAsync`.

Each delivery attempt returns a `DeliveryResult(Success, ProviderId, ErrorMessage)`. The pipeline collects errors and delivery IDs.

**Partial success**: if some deliveries succeed and some fail, the result is still `Success = true` with the errors attached. Only when *all* deliveries fail does the result become `Success = false`.

**Zero deliveries is a failure.** If nothing was even attempted (no channel is registered for the
resolved channels, or the recipient's preferences suppressed every one), the result is `Success = false`
and the error distinguishes the two causes. Reporting success there would tell the caller the
notification went out while it silently evaporated, which is precisely what an app that forgot to
register a channel needs to be told. The tracking record for the same operation is settled to `Failed`
in that case, so result and record always agree.

### 5. Track

Before each delivery attempt, a `NotificationRecord` with `Status = Pending` is created via `INotificationStore.CreateAsync`. After delivery, the status is updated to `Sent` or `Failed`.

The record includes: audience, recipient address, channel, subject, category, tenant ID, metadata, and lifecycle timestamps (`CreatedAt`, `SentAt`, `DeliveredAt`, `ReadAt`).

## Audiences

The `NotificationAudience` enum categorizes the intent of a notification:

| Audience | Description | Typical Channel |
|----------|-------------|-----------------|
| `EndUser` | Application customer (hotel guest, subscriber) | Email, Push, SMS |
| `Admin` | Platform operator, ops team | Email, Slack |
| `Developer` | DevOps, CI/CD systems | Webhook, Slack |
| `TenantAdmin` | Multi-tenant administrator | Email, InApp |
| `System` | Monitoring, health checks | Webhook |

The audience affects recipient resolution (the resolver may query different stores for different audiences) and can inform custom routing logic.

## Channels

`NotificationChannel` is a `[Flags]` enum with six values:

```csharp
[Flags]
public enum NotificationChannel
{
    None    = 0,
    Email   = 1 << 0,   // SMTP via Pragmatic.Email
    Sms     = 1 << 1,   // SMS provider
    Push    = 1 << 2,   // Push notification
    InApp   = 1 << 3,   // In-application notification center
    Webhook = 1 << 4,   // HTTP POST
    Slack   = 1 << 5,   // Slack webhook
}
```

Because it is a flags enum, routing can return `Email | Push` for `High` priority, and the pipeline will deliver to both channels independently.

### Built-in Channels

**SmtpChannel** (`Pragmatic.Notifications`): Delegates to `Pragmatic.Email`'s `IEmailSender` for SMTP delivery, connection pooling, DKIM, and middleware. Configuration splits sender identity (`SmtpOptions`) from transport settings (`SmtpTransportOptions`).

**WebhookChannel** (`Pragmatic.Notifications.Webhook`): Sends an HTTP POST with JSON payload. Uses a named `HttpClient` (`Pragmatic.Notifications.Webhook`). If `NotificationContent.JsonPayload` is set, it sends the raw payload; otherwise it serializes `{ Subject, Body, Timestamp }` using AOT-safe `JsonSerializerContext`.

### Custom Channels

Implement `INotificationChannel` and register via `builder.AddChannel<T>()`:

```csharp
public sealed class PushChannel : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Push;

    public async Task<DeliveryResult> DeliverAsync(
        ResolvedRecipient recipient, NotificationContent content, CancellationToken ct)
    {
        // Deliver to push service
        return DeliveryResult.Succeeded(pushMessageId);
    }
}
```

## Delivery Lifecycle

```
Pending  -->  Sent  -->  Delivered  -->  Read
   |                         |
   +--->  Failed            Bounced
```

| Status | Meaning |
|--------|---------|
| `Pending` | Record created, delivery not yet attempted |
| `Sent` | Channel provider accepted the message |
| `Delivered` | Confirmed delivery (webhook: HTTP 2xx; email: accepted by relay) |
| `Read` | Recipient opened/read the notification (requires read-tracking integration) |
| `Failed` | Delivery attempt failed (error message stored) |
| `Bounced` | Email bounce or permanent delivery failure |

## Background Processing

`EnqueueAsync` writes to a `BoundedChannel<NotificationRequest>` (default capacity: 1000). A `BackgroundService` (`NotificationDeliveryWorker`) reads from this channel and processes through the same pipeline. This provides non-blocking fire-and-forget semantics with backpressure.

```csharp
// Returns immediately with a tracking ID
var result = await notificationService.EnqueueAsync(request);
// result.NotificationId is available for polling
```

The worker catches exceptions per-request and logs them without crashing the background loop.

## Multi-Tenant Support

The `INotificationChannelFactory.GetChannel(channel, tenantId)` signature supports per-tenant channel resolution. The `DefaultChannelFactory` ignores `tenantId`, but a custom factory can return different SMTP configurations per tenant.

`NotificationRecord.TenantId` is indexed in the EF Core store for efficient per-tenant queries.

## Preferences Model

`NotificationPreferences` controls per-user delivery behavior:

- `Enabled` -- global kill switch; if `false`, no notifications are delivered
- `PreferredChannel` -- used when priority allows choice (Normal)
- `MutedCategories` -- set of category strings the user has opted out of
- `DoNotDisturb` -- holds non-critical notifications (reserved for future digest support)
- `Locale` -- for future localized template rendering

Implement `INotificationPreferenceProvider` to source preferences from your user profile database. The `DefaultPreferenceProvider` returns all-enabled defaults.
