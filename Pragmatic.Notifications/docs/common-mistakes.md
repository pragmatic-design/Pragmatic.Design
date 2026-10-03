# Common Mistakes

## 1. Using SendAsync in Event Handlers

```csharp
// WRONG: blocks event processing while waiting for SMTP delivery
[EventHandler]
public sealed partial class Handler(INotificationService svc)
    : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed e, CancellationToken ct)
    {
        await svc.SendAsync(new NotificationRequest { ... }, ct); // blocks
    }
}
```

```csharp
// CORRECT: enqueue for background delivery
public async Task HandleAsync(ReservationConfirmed e, CancellationToken ct)
{
    await svc.EnqueueAsync(new NotificationRequest { ... }, ct); // returns immediately
}
```

`SendAsync` waits for the SMTP server response. In event handlers (which may run inside a database transaction or block other handlers), use `EnqueueAsync` to offload delivery to the background worker.

## 2. Using EnqueueAsync in Jobs

```csharp
// WRONG: double-queuing; the job runner already handles retry/background
[Job]
public sealed partial class ReminderJob(INotificationService svc) : IJob<ReminderParams>
{
    public async Task ExecuteAsync(ReminderParams p, JobContext ctx, CancellationToken ct)
    {
        await svc.EnqueueAsync(new NotificationRequest { ... }, ct);
    }
}
```

```csharp
// CORRECT: use SendAsync; the job runner provides retry and scheduling
public async Task ExecuteAsync(ReminderParams p, JobContext ctx, CancellationToken ct)
{
    await svc.SendAsync(new NotificationRequest { ... }, ct);
}
```

Jobs already execute in the background with retry policies (`[Retry]`). Using `EnqueueAsync` means the job completes instantly without knowing if delivery succeeded, defeating the retry mechanism.

## 3. Forgetting to Register a Channel

```csharp
// WRONG: no channel registered, so all notifications are silently skipped
app.UseNotifications(n =>
{
    // No AddSmtp(), AddWebhook(), or AddChannel<T>()
});
```

If no `INotificationChannel` is registered, `DefaultChannelFactory.GetRegisteredChannels()` returns an empty list, and every notification routes to `NotificationChannel.None`. Check logs for the warning: "Channel {Channel} is not registered -- skipping".

## 4. Empty Subject or Body

```csharp
// WRONG: validation rejects empty subject
var request = new NotificationRequest
{
    Audience = NotificationAudience.EndUser,
    Recipient = NotificationRecipient.Direct("guest@hotel.com"),
    Content = new NotificationContent
    {
        Subject = "",  // validation fails
        Body = "Your reservation is confirmed.",
    },
};
var result = await svc.SendAsync(request);
// result.Success == false, result.Errors == ["Notification subject is required."]
```

The pipeline validates that both `Subject` and `Body` are non-empty before any processing. Always provide both.

## 5. Not Checking NotificationResult

```csharp
// WRONG: ignoring the result (silent failures)
await notificationService.SendAsync(request);
```

```csharp
// CORRECT: check the result
var result = await notificationService.SendAsync(request);
if (!result.Success)
{
    logger.LogWarning("Notification failed: {Errors}", string.Join(", ", result.Errors ?? []));
}
```

`SendAsync` returns `NotificationResult` with `Success`, `Errors`, and `DeliveryIds`. Even partial failures (some channels succeed, some fail) set `Success = true` with errors attached. Fully failed deliveries set `Success = false`.

## 6. Using InMemoryNotificationStore in Production

```csharp
// WRONG: notifications are lost on restart, no cross-instance visibility
app.UseNotifications(n =>
{
    n.AddSmtp(sender => { ... }, transport => { ... });
    // InMemoryNotificationStore is the default
});
```

```csharp
// CORRECT: use EF Core store for durable tracking
app.UseNotifications(n =>
{
    n.AddSmtp(sender => { ... }, transport => { ... });
    n.UseEfCoreStore(db => db.UseNpgsql(connectionString));
});
```

The `InMemoryNotificationStore` is suitable for development only. It loses all data on restart and is not shared across instances. In production, use `UseEfCoreStore()` for the `__Notifications` table.

## 7. Hardcoding Recipient Addresses Instead of Using Audiences

```csharp
// WRONG: hardcoded email, no preference support, no user resolution
var request = new NotificationRequest
{
    Audience = NotificationAudience.System, // misleading audience
    Recipient = NotificationRecipient.Direct("admin@hotel.com"),
    Content = new NotificationContent { Subject = "Alert", Body = "Overbooking" },
};
```

```csharp
// CORRECT: use role-based targeting
var request = new NotificationRequest
{
    Audience = NotificationAudience.Admin,
    Recipient = NotificationRecipient.Role("hotel-manager"),
    Content = new NotificationContent { Subject = "Overbooking Alert", Body = "..." },
    Priority = NotificationPriority.Critical,
};
```

Direct addresses bypass user resolution and preferences. When targeting known user groups, use `NotificationRecipient.User()`, `Role()`, or `Tenant()` so the pipeline can resolve addresses dynamically and apply preference filtering.

## 8. Not Providing HtmlBody for Email

```csharp
// SUBOPTIMAL: email clients render plain text poorly
new NotificationContent
{
    Subject = "Reservation Confirmed",
    Body = "Your reservation from Mar 15 to Mar 20 is confirmed.",
    // HtmlBody = null: email will be plain text only
}
```

```csharp
// BETTER: provide both Body (fallback) and HtmlBody
new NotificationContent
{
    Subject = "Reservation Confirmed",
    Body = "Your reservation from Mar 15 to Mar 20 is confirmed.",
    HtmlBody = """
        <h2>Reservation Confirmed</h2>
        <p>Your reservation from <strong>Mar 15</strong> to <strong>Mar 20</strong> is confirmed.</p>
        """,
}
```

The `SmtpChannel` uses `Body` as the plain text part and `HtmlBody` as the HTML part. Most email clients prefer HTML. Always provide both for best rendering.

## 9. Overriding Channel Without Checking Registration

```csharp
// WRONG: Slack is not registered, override forces it, delivery silently skipped
var request = new NotificationRequest
{
    Audience = NotificationAudience.Developer,
    Recipient = NotificationRecipient.Direct("dev@company.com"),
    Content = new NotificationContent { Subject = "Build Failed", Body = "..." },
    ChannelOverride = NotificationChannel.Slack, // not registered
};
```

`ChannelOverride` bypasses the router but not the channel factory. If the overridden channel has no registered `INotificationChannel`, the pipeline logs a warning and skips delivery. Only override to channels you know are registered.

## 10. Setting Category Without Preference Integration

```csharp
// MISLEADING: categories only work when a custom INotificationPreferenceProvider is registered
var request = new NotificationRequest
{
    Category = "marketing", // ignored by DefaultPreferenceProvider
    // ...
};
```

The `Category` field is used by the router to check `MutedCategories` in user preferences. The `DefaultPreferenceProvider` returns empty preferences (no muted categories), so setting `Category` has no effect until you implement `INotificationPreferenceProvider` and users can manage their muted categories.

## 11. Sending High-Volume Notifications Synchronously

```csharp
// WRONG: sends 500 emails synchronously, blocking the request
foreach (var guest in guests)
{
    await notificationService.SendAsync(new NotificationRequest
    {
        Recipient = NotificationRecipient.Direct(guest.Email),
        // ...
    });
}
```

```csharp
// CORRECT: enqueue for background processing
foreach (var guest in guests)
{
    await notificationService.EnqueueAsync(new NotificationRequest
    {
        Recipient = NotificationRecipient.Direct(guest.Email),
        // ...
    });
}
```

For bulk notifications, always use `EnqueueAsync`. The `BoundedChannel` has backpressure (default capacity: 1000), so the producer waits if the channel is full rather than running out of memory.
