# Troubleshooting

## Frequently Asked Questions

### Notifications are silently dropped -- nothing in logs

**Cause**: No `INotificationChannel` is registered.

The `DefaultChannelFactory` returns `null` for unregistered channels, and the pipeline logs a warning: "Channel {Channel} is not registered -- skipping". If you see no logs at all, check that you have registered at least one channel:

```csharp
app.UseNotifications(n =>
{
    n.AddSmtp(sender => { ... }, transport => { ... }); // registers Email channel
    // or n.AddWebhook();
    // or n.AddChannel<MyCustomChannel>();
});
```

### SendAsync returns Success=false with "No recipients resolved"

**Cause**: The `DefaultRecipientResolver` cannot resolve the recipient type.

The default resolver only handles direct addresses (`EmailAddress`, `PhoneNumber`, `WebhookUrl`) and passes through `UserId` as-is. If you use `NotificationRecipient.Role("admin")` or `NotificationRecipient.Tenant("tenant-1")`, the default resolver returns zero results.

**Fix**: Register a custom `IRecipientResolver` that queries your identity system:

```csharp
services.AddSingleton<IRecipientResolver, IdentityRecipientResolver>();
```

### SendAsync returns Success=false with "Notification subject is required"

**Cause**: `NotificationContent.Subject` is an empty string.

The pipeline validates that both `Subject` and `Body` are non-empty. This is the first step -- if validation fails, no records are created and no delivery is attempted.

### EnqueueAsync succeeds but notification is never delivered

**Possible causes**:

1. **Worker not running**: The `NotificationDeliveryWorker` is registered as a `BackgroundService`. Verify it starts by checking for the log message "Notification delivery worker started".

2. **Channel full**: The `BoundedChannel` default capacity is 1000 with `FullMode = Wait`. If the channel is full and the producer is waiting, the `EnqueueAsync` call blocks until space is available. Check `NotificationOptions.DeliveryChannelCapacity` if you need higher throughput.

3. **Silent delivery failure**: The worker catches exceptions and logs them. Check for "Background notification delivery failed" or "Background notification delivery exception" in logs.

### Email delivery fails with connection error

**Cause**: SMTP transport misconfigured.

The `SmtpChannel` delegates to `Pragmatic.Email`'s `IEmailSender`. Transport settings (host, port, SSL, authentication) are configured via `SmtpTransportOptions`:

```csharp
n.AddSmtp(
    sender => { sender.SenderAddress = "noreply@hotel.com"; },
    transport =>
    {
        transport.Host = "smtp.hotel.com";
        transport.Port = 587;
        transport.UseSsl = true;
        transport.Username = "user";
        transport.Password = "pass";
    });
```

If you only configure the sender (one-arg `AddSmtp`), the transport options must be configured separately via `Pragmatic.Email`'s `UseSmtp()`.

### Webhook returns HTTP 4xx/5xx but I see "Succeeded"

`DeliveryResult.Succeeded()` is only returned when `response.IsSuccessStatusCode` is true. If you see a successful `NotificationResult`, the webhook HTTP response was 2xx. Check:

- The webhook URL is correct in `NotificationRecipient.WebhookUrl`
- The `JsonPayload` is valid JSON (if using custom payload)
- The receiving service is running

For non-2xx responses, the `DeliveryResult` contains `"HTTP {StatusCode}: {responseBody}"`.

### Preferences are not being applied -- all notifications go through

**Cause**: `DefaultPreferenceProvider` returns all-enabled defaults.

The default provider always returns `new NotificationPreferences()` (enabled, no muted categories). To use actual preferences, implement and register `INotificationPreferenceProvider`:

```csharp
n.UsePreferences<UserProfilePreferenceProvider>();
```

### EF Core store throws "No database provider configured"

**Cause**: `UseEfCoreStore()` called without a database configuration.

```csharp
// WRONG: no database provider
n.UseEfCoreStore();

// CORRECT: provide database configuration
n.UseEfCoreStore(db => db.UseNpgsql(connectionString));
```

If your application already has a `NotificationDbContext` registered via another mechanism, you can call `UseEfCoreStore()` without the lambda -- but the `IDbContextFactory<NotificationDbContext>` must be registered.

### Multiple channels fire for Normal priority

The `DefaultNotificationRouter` selects a single channel for `Normal` priority: the recipient's resolved channel, or email as fallback. Multiple channels fire only for:

- `High` priority: Email + Push
- `Critical` priority: All registered channels

If you see multiple deliveries for `Normal`, check:
- `ChannelOverride` is not set to a combined flags value
- A custom `INotificationRouter` is not registered

### Test harness shows 0 sent notifications

**Cause**: `INotificationService` was not resolved from the service provider where the harness is registered.

```csharp
// WRONG: resolving from a different container
var services = new ServiceCollection();
var harness = services.AddNotificationTestHarness();
// ... but the SUT uses a different IServiceProvider

// CORRECT: ensure the SUT resolves INotificationService from the same container
var sp = services.BuildServiceProvider();
var svc = sp.GetRequiredService<INotificationService>(); // this is the harness
```

The harness replaces `INotificationService` via `RemoveAll` + `AddSingleton`. If your test uses a `WebApplicationFactory` or similar, ensure the harness is registered in the test host's service collection.

## Diagnostics & Observability

### OpenTelemetry Metrics

The `NotificationsDiagnostics` class exposes metrics under the `Pragmatic.Notifications` source:

| Metric | Type | Description |
|--------|------|-------------|
| `pragmatic.notifications.sent` | Counter | Successfully sent notifications |
| `pragmatic.notifications.failed` | Counter | Failed notifications |
| `pragmatic.notifications.enqueued` | Counter | Notifications enqueued for background delivery |
| `pragmatic.notifications.delivery.duration` | Histogram | Delivery duration in milliseconds |
| `pragmatic.notifications.channel.deliveries` | Counter | Per-channel successful deliveries |
| `pragmatic.notifications.channel.failures` | Counter | Per-channel failures |

Subscribe to the meter in your OpenTelemetry configuration:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Pragmatic.Notifications"));
```

### Activity Tracing

The `NotificationsDiagnostics.ActivitySource` (named `Pragmatic.Notifications`) can be subscribed for distributed tracing:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Pragmatic.Notifications"));
```

### Log Messages

The pipeline and channels use `[LoggerMessage]` for structured logging. Key messages:

| Level | Message | Meaning |
|-------|---------|---------|
| Warning | "No recipients resolved for audience {Audience}" | Resolver returned 0 results |
| Warning | "Channel {Channel} is not registered -- skipping" | No provider for that channel |
| Information | "Notification delivered via {Channel} to {Address}" | Successful delivery |
| Warning | "Notification delivery failed via {Channel} to {Address}: {Error}" | Channel returned failure |
| Error | "Notification delivery exception via {Channel} to {Address}" | Unhandled exception during delivery |
| Information | "Notification email sent to {Address}" | SMTP delivery success |
| Warning | "Notification email failed to {Address}: {Error}" | SMTP delivery failure |
| Information | "Webhook delivered to {Url}" | Webhook HTTP 2xx |
| Warning | "Webhook delivery failed to {Url}: HTTP {StatusCode}" | Webhook HTTP error |
| Information | "Notification delivery worker started/stopped" | Background worker lifecycle |

### Querying Delivery Status

With EF Core store, query the `__Notifications` table:

```sql
-- Failed deliveries in the last hour
SELECT * FROM "__Notifications"
WHERE "Status" = 'Failed'
  AND "CreatedAt" > NOW() - INTERVAL '1 hour'
ORDER BY "CreatedAt" DESC;

-- Pending (stuck) deliveries
SELECT * FROM "__Notifications"
WHERE "Status" = 'Pending'
  AND "CreatedAt" < NOW() - INTERVAL '5 minutes';

-- Per-channel delivery counts
SELECT "Channel", "Status", COUNT(*)
FROM "__Notifications"
WHERE "CreatedAt" > NOW() - INTERVAL '24 hours'
GROUP BY "Channel", "Status";
```

Programmatically:

```csharp
var pending = await store.GetPendingAsync(limit: 50);
var record = await store.GetByIdAsync(notificationId);
```
