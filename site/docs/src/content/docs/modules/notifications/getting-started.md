---
title: "Getting Started"
description: "Step-by-step guide to integrating Pragmatic.Notifications into a hotel booking application, matching the Showcase project."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Notifications/docs/getting-started.md
sidebar:
  order: 2
---
Step-by-step guide to integrating Pragmatic.Notifications into a hotel booking application, matching the Showcase project.

## 1. Install Packages

```bash
dotnet add package Pragmatic.Notifications
```

For production, add persistent tracking:

```bash
dotnet add package Pragmatic.Notifications.EFCore
```

For webhook delivery:

```bash
dotnet add package Pragmatic.Notifications.Webhook
```

## 2. Register in Program.cs

Minimal setup with SMTP email:

```csharp
app.UseNotifications(n =>
{
    n.AddSmtp(
        sender =>
        {
            sender.SenderAddress = "noreply@showcase.pragmatic.design";
            sender.SenderName = "Showcase Hotel";
        },
        transport =>
        {
            transport.Host = app.Configuration["Smtp:Host"] ?? "localhost";
            transport.Port = int.TryParse(app.Configuration["Smtp:Port"], out var p) ? p : 587;
            transport.UseSsl = !app.Environment.IsDevelopment();
            transport.MaxConnections = 3;
        });
});
```

This registers:
- `INotificationService` (core service)
- `SmtpChannel` (email delivery via Pragmatic.Email)
- `NotificationPipeline` (validate/resolve/route/deliver/track)
- `NotificationDeliveryWorker` (background service for `EnqueueAsync`)
- `InMemoryNotificationStore` (default tracking -- swap for EF Core in production)

## 3. Inject and Send

### Direct Email (Simplest)

```csharp
public sealed class BookingService(INotificationService notifications)
{
    public async Task ConfirmBooking(Reservation reservation)
    {
        // ... save reservation ...

        await notifications.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct(reservation.GuestEmail),
            Content = new NotificationContent
            {
                Subject = $"Booking Confirmed - {reservation.CheckIn:D}",
                Body = $"Your booking from {reservation.CheckIn:D} to {reservation.CheckOut:D} is confirmed.",
            },
            Category = "transactional",
        });
    }
}
```

### User-Targeted (by ID)

> **Requires a recipient resolver.** The built-in resolver only understands direct addresses: it has
> no way of knowing where your users are stored. Targeting `User`, `Users`, `Role` or `Tenant` without
> registering an `IRecipientResolver` resolves to zero recipients and the send **fails** with an
> explanatory error. See [Resolving recipients](#resolving-recipients).

```csharp
await notifications.EnqueueAsync(new NotificationRequest
{
    Audience = NotificationAudience.EndUser,
    Recipient = NotificationRecipient.User(guestId.ToString()),
    Content = new NotificationContent
    {
        Subject = "Welcome to Showcase Hotel",
        Body = "Thank you for creating your account.",
    },
    Priority = NotificationPriority.Low,
    Category = "onboarding",
});
```

### Admin Alert

```csharp
await notifications.SendAsync(new NotificationRequest
{
    Audience = NotificationAudience.Admin,
    Recipient = NotificationRecipient.Role("hotel-manager"),
    Content = new NotificationContent
    {
        Subject = "Overbooking Detected",
        Body = $"Room {roomNumber} is double-booked for {date:D}.",
    },
    Priority = NotificationPriority.Critical,
});
```

### Webhook (System Integration)

```csharp
await notifications.SendAsync(new NotificationRequest
{
    Audience = NotificationAudience.System,
    Recipient = NotificationRecipient.ToWebhook("https://hooks.example.com/bookings"),
    Content = new NotificationContent
    {
        Subject = "Reservation Created",
        Body = "New reservation",
        JsonPayload = JsonSerializer.Serialize(new { reservationId, guestId, checkIn, checkOut }),
    },
});
```

## 4. Integration with Events

The Showcase project uses domain events to trigger notifications. When a reservation is confirmed, an event handler enqueues the email:

```csharp
[EventHandler]
public sealed partial class ReservationConfirmedNotificationHandler(
    INotificationService notificationService) : IDomainEventHandler<ReservationConfirmed>
{
    public async Task HandleAsync(ReservationConfirmed @event, CancellationToken ct)
    {
        await notificationService.EnqueueAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.User(@event.GuestId.ToString()),
            Content = new NotificationContent
            {
                Subject = $"Reservation Confirmed - {FormatDates(@event.CheckIn, @event.CheckOut)}",
                Body = $"""
                    Your reservation has been confirmed.
                    Check-in:  {@event.CheckIn:D}
                    Check-out: {@event.CheckOut:D}
                    Total:     {@event.TotalAmount:N2} {@event.Currency}
                    """,
                HtmlBody = $"""
                    <h2>Reservation Confirmed</h2>
                    <p>Check-in: <strong>{@event.CheckIn:D}</strong></p>
                    <p>Total: {@event.TotalAmount:N2} {@event.Currency}</p>
                    """,
            },
            Category = "transactional",
        }, ct);
    }

    private static string FormatDates(DateTimeOffset checkIn, DateTimeOffset checkOut)
        => $"{checkIn:MMM dd} - {checkOut:MMM dd, yyyy}";
}
```

Key pattern: use `EnqueueAsync` (not `SendAsync`) in event handlers so notification delivery does not block domain event processing.

## 5. Integration with Jobs

Schedule a delayed notification using Pragmatic.Jobs:

```csharp
[Job]
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
public sealed partial class SendCheckInReminderJob(
    INotificationService notificationService) : IJob<CheckInReminderParams>
{
    public async Task ExecuteAsync(CheckInReminderParams p, JobContext context, CancellationToken ct)
    {
        await notificationService.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct(p.GuestEmail),
            Content = new NotificationContent
            {
                Subject = $"Check-in Reminder - {p.CheckIn:D}",
                Body = "Your check-in is tomorrow. We look forward to welcoming you!",
            },
            Priority = NotificationPriority.High,
            Category = "transactional",
        }, ct);
    }
}
```

Key pattern: use `SendAsync` (not `EnqueueAsync`) in jobs because the job runner already provides retry and background execution.

## 6. Add EF Core Tracking (Production)

```csharp
app.UseNotifications(n =>
{
    n.AddSmtp(sender => { ... }, transport => { ... });

    n.UseEfCoreStore(db => db.UseNpgsql(
        app.Configuration.GetConnectionString("Notifications")));
});
```

This stores all delivery attempts in the `__Notifications` table with status tracking, timestamps, and indexes for efficient querying.

## 7. Add Webhook Channel

```csharp
app.UseNotifications(n =>
{
    n.AddSmtp(sender => { ... }, transport => { ... });
    n.AddWebhook();
});
```

The webhook channel sends HTTP POST requests with JSON body to the URL specified in `NotificationRecipient.WebhookUrl`. It uses a named `HttpClient` (`Pragmatic.Notifications.Webhook`) that you can configure via `IHttpClientFactory`.

## 8. Testing

Replace `INotificationService` with the test harness:

```csharp
public class ReservationNotificationTests
{
    [Fact]
    public async Task ConfirmReservation_SendsConfirmationEmail()
    {
        // Arrange
        var services = new ServiceCollection();
        var harness = services.AddNotificationTestHarness();
        // ... register other dependencies ...
        var sp = services.BuildServiceProvider();

        var svc = sp.GetRequiredService<INotificationService>();

        // Act
        await svc.SendAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct("guest@example.com"),
            Content = new NotificationContent
            {
                Subject = "Reservation Confirmed",
                Body = "Your reservation has been confirmed.",
            },
            Category = "transactional",
        });

        // Assert
        harness.Sent.Should().HaveCount(1);
        harness.HasSentWithSubject("Reservation Confirmed").Should().BeTrue();
        harness.HasSentTo(NotificationAudience.EndUser).Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueAsync_RecordsSynchronousFlag()
    {
        var services = new ServiceCollection();
        var harness = services.AddNotificationTestHarness();
        var sp = services.BuildServiceProvider();
        var svc = sp.GetRequiredService<INotificationService>();

        await svc.EnqueueAsync(new NotificationRequest { /* ... */ });

        harness.Sent[0].Synchronous.Should().BeFalse();
    }
}
```

### Harness API

| Method | Description |
|--------|-------------|
| `Sent` | All recorded notifications |
| `HasSentTo(audience)` | Any notification to that audience |
| `HasSentWithSubject(subject)` | Any notification with that subject |
| `HasSent(predicate)` | Custom predicate match |
| `SentWhere(predicate)` | Filter recorded notifications |
| `Reset()` | Clear all recordings |

## 9. Custom Preference Provider

```csharp
public sealed class UserProfilePreferenceProvider(IUserStore userStore)
    : INotificationPreferenceProvider
{
    public async Task<NotificationPreferences?> GetPreferencesAsync(
        string userIdOrAddress, CancellationToken ct)
    {
        var user = await userStore.FindByEmailOrIdAsync(userIdOrAddress, ct);
        if (user is null) return null;

        return new NotificationPreferences
        {
            Enabled = user.NotificationsEnabled,
            PreferredChannel = user.PreferredNotificationChannel,
            MutedCategories = user.MutedNotificationCategories,
            DoNotDisturb = user.DoNotDisturb,
            Locale = user.Locale,
        };
    }
}
```

Register:

```csharp
app.UseNotifications(n =>
{
    n.UsePreferences<UserProfilePreferenceProvider>();
    // ...
});
```

## 10. Resolving recipients

<a id="resolving-recipients"></a>

The built-in `DefaultRecipientResolver` handles only direct addresses: `Direct(email)`,
`ToWebhook(url)` and a phone number. `User`, `Users`, `Role` and `Tenant` cannot be resolved by the
library (it does not know where your users live), so they yield zero recipients and the send fails
with an error saying exactly that. Registering a resolver is what makes those forms work:

```csharp
public sealed class IdentityRecipientResolver(
    IUserStore userStore,
    INotificationPreferenceProvider prefs) : IRecipientResolver
{
    public async Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
        NotificationRecipient recipient, NotificationAudience audience, CancellationToken ct)
    {
        var results = new List<ResolvedRecipient>();

        if (recipient.UserId is { } userId)
        {
            var user = await userStore.GetByIdAsync(userId, ct);
            if (user is not null)
            {
                var userPrefs = await prefs.GetPreferencesAsync(userId, ct);
                results.Add(new ResolvedRecipient(
                    user.Email, NotificationChannel.Email, user.Locale, user.TenantId, userPrefs));
            }
        }

        if (recipient.RoleName is { } role)
        {
            var users = await userStore.GetByRoleAsync(role, ct);
            foreach (var user in users)
            {
                var userPrefs = await prefs.GetPreferencesAsync(user.Id, ct);
                results.Add(new ResolvedRecipient(
                    user.Email, NotificationChannel.Email, user.Locale, user.TenantId, userPrefs));
            }
        }

        // Direct addresses pass through
        if (recipient.EmailAddress is not null)
            results.Add(new ResolvedRecipient(recipient.EmailAddress, NotificationChannel.Email, null, null, null));

        if (recipient.WebhookUrl is not null)
            results.Add(new ResolvedRecipient(recipient.WebhookUrl, NotificationChannel.Webhook, null, null, null));

        return results;
    }
}
```

Register it through the builder:

```csharp
app.UseNotifications(n => n.UseRecipientResolver<IdentityRecipientResolver>());
```

The resolver is registered **scoped**, so it can depend on a `DbContext` or a repository directly.

### Resolving during background delivery

`EnqueueAsync` delivers on a background worker, outside the originating request. Two consequences:

- **Tenant.** The worker restores the tenant captured at enqueue time onto the delivery scope, so
  tenant-filtered queries return the right rows. Nothing to do on your side.
- **No acting user.** Ownership and data-scope filters (`[HasOwner]`, `[HasAccessScopes]`) hide rows
  from a caller that is not a user, so a plain lookup finds nothing. Read under an elevated filter
  mode, which keeps tenant isolation intact:

```csharp
using var elevated = filterToggle.UseMode(FilterMode.Admin);
var user = await users.GetByIdAsync(id, ct);
```
