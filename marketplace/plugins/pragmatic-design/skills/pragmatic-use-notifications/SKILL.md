---
name: pragmatic-use-notifications
description: Use when the app sends email, webhook, Slack or SMS notifications through one API with routing, user preferences and delivery tracking — Pragmatic.Notifications. A single mail is pragmatic-use-email.
---

# Pragmatic Use Notifications

**Covers:** Send notifications through one pipeline with Pragmatic.Notifications from NuGet — channel routing (email/webhook/Slack/SMS), user preferences, delivery tracking, background processing. Channels configured once in the host.

`Pragmatic.Notifications` is one pipeline: send a notification, channel routing + user preferences +
delivery tracking + background dispatch are handled for you. Add a channel without touching call sites.

## When to use

- Sending transactional or broadcast notifications across one or more channels.
- Enforcing user preferences / do-not-disturb centrally.

## Packages

```xml
<PackageReference Include="Pragmatic.Notifications" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Notifications.Webhook" Version="1.0.0-alpha.1" />  <!-- optional -->
<PackageReference Include="Pragmatic.Notifications.Slack" Version="1.0.0-alpha.1" />    <!-- optional -->
<PackageReference Include="Pragmatic.Notifications.Sms" Version="1.0.0-alpha.1" />      <!-- optional -->
<PackageReference Include="Pragmatic.Notifications.EFCore" Version="1.0.0-alpha.1" />   <!-- optional -->
```

The SMTP channel is part of `Pragmatic.Notifications`. There is **no** `Pragmatic.Notifications.Email`
package.

## Core pattern

Inject `INotificationService` and send a `NotificationRequest`. There is no "notification type" to
declare — the request carries audience, recipient and content:

```csharp
await notifier.SendAsync(new NotificationRequest
{
    Audience  = NotificationAudience.EndUser,
    Recipient = NotificationRecipient.Direct(guest.Email),
    Content   = new NotificationContent
    {
        Subject   = "Reservation confirmed",
        Body      = "Your reservation has been confirmed.",  // required, plain text
        HtmlBody  = "<h1>Reservation confirmed</h1>",        // optional, used by the e-mail channel
        ShortBody = "Booking confirmed",                     // optional, preferred by SMS (160 chars)
    },
    Category = "transactional",     // matched against the user's muted categories
    Priority = NotificationPriority.Normal,
}, ct);
```

`Audience`, `Recipient` and `Content` are required; `Subject` and `Body` must both be non-empty or the
send fails validation.

The literals above are for illustration. In an application the content comes from a `.pdxemail`
template in the recipient's language (`pragmatic-use-documents`):
`var mail = await templates.EmailAsync("reservation-confirmed.pdxemail", guest.Language, data, ct);`, then
`Subject = mail.Subject, Body = mail.Text, HtmlBody = mail.Html`. The Showcase's confirmation and
check-in reminder are this shape.

Use `EnqueueAsync` for fire-and-forget: it returns immediately with a tracking id and the background
worker delivers.

```csharp
var result = await notifier.EnqueueAsync(request, ct);
// result.NotificationId → look it up later via INotificationStore
```

## Reading the outcome

`SendAsync` returns success only if at least one delivery was attempted and succeeded.

| Outcome | Meaning |
|---|---|
| `Success = true`, `Errors = null` | every delivery succeeded |
| `Success = true`, `Errors` populated | partial: some channels failed, at least one worked |
| `Success = false` | nothing was delivered — read `Errors` for the reason |

Zero deliveries is a **failure**, not a success: it means no channel was registered, or the recipient's
preferences suppressed everything. Do not treat a send as fire-and-forget-and-ignore.

## Wiring channels (host)

```csharp
app.UseNotifications(n =>
{
    n.AddSmtp(
        sender    => { sender.SenderAddress = cfg["Smtp:SenderAddress"]!; sender.SenderName = "My App"; },
        transport => { transport.Host = cfg["Smtp:Host"]!; transport.Port = 587; transport.UseSsl = true; });

    n.AddWebhook(w => w.SigningSecret = cfg["Webhook:Secret"]);   // Pragmatic.Notifications.Webhook

    n.AddSlack(s => s.DefaultWebhookUrl = cfg["Slack:WebhookUrl"]);   // Pragmatic.Notifications.Slack

    n.AddTwilioSms(sms =>                                            // Pragmatic.Notifications.Sms
    {
        sms.AccountSid = cfg["Twilio:AccountSid"]!;
        sms.AuthToken  = cfg["Twilio:AuthToken"]!;
        sms.FromNumber = cfg["Twilio:FromNumber"]!;
    });

    n.UseEfCoreStore(db => db.UseNpgsql(cfg.GetConnectionString("App")!));  // durable tracking
});
```

`AddSmtp` takes the sender and the transport settings together — there is no overload without the
transport.

### Durable tracking needs a boundary that holds the table

`UseEfCoreStore` wires the EF store; it does not create `__Notifications`. In a Pragmatic host the
schema comes from the generated boundary contexts, so one boundary declares that it holds the records:

```csharp
[Boundary]
[StoresNotifications]            // __Notifications is mapped and migrated with this boundary's data
public partial class BookingBoundary;
```

Point `UseEfCoreStore` at that boundary's database (the same connection string). The boundary project
references `Pragmatic.Notifications.EFCore`; without it the attribute maps nothing, and the generator
reports **PRAG2100** (Warning) on the boundary.
Without the attribute, the first send fails with `relation "__Notifications" does not exist`.

## Addressing a user, role or tenant

`NotificationRecipient` offers `Direct`, `ToWebhook`, `User`, `Users`, `Role` and `Tenant`, but only the
direct forms work out of the box — the library cannot know where your users live. `User`, `Users`,
`Role` and `Tenant` resolve to nothing unless you register a resolver, and the send fails with an
explanatory error:

```csharp
public sealed class AppRecipientResolver(IRepository<User> users) : IRecipientResolver
{
    public async Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(
        NotificationRecipient recipient, NotificationAudience audience, CancellationToken ct = default)
    {
        if (recipient.UserId is null || !Guid.TryParse(recipient.UserId, out var id))
            return [];

        var user = await users.GetByIdAsync(id, ct);
        return user is null
            ? []
            : [new ResolvedRecipient(user.Email, NotificationChannel.Email, user.Locale, null, null)];
    }
}

// host
app.UseNotifications(n => n.UseRecipientResolver<AppRecipientResolver>());
```

The resolver is registered **scoped**, so it may depend on a DbContext or repository.

Two things to get right when the notification is delivered in the background (`EnqueueAsync`):

- the worker restores the tenant captured at enqueue time, so tenant-filtered queries work;
- there is no acting user, so entities behind ownership/scope filters are invisible — read them under
  `IQueryFilterToggle.UseMode(FilterMode.Admin)`, which keeps tenant isolation while bypassing
  per-user visibility.

## Channels that exist

Provided: **e-mail (SMTP)**, **webhook**, **Slack** (incoming webhooks) and **SMS** (Twilio REST, no
vendor SDK). `NotificationChannel` also declares `Push` and `InApp` — extension points with no
implementation, so selecting one without registering a provider makes the send fail. Add your own:

```csharp
public sealed class PushChannel(IHttpClientFactory http) : INotificationChannel
{
    public NotificationChannel Channel => NotificationChannel.Push;

    public async Task<DeliveryResult> DeliverAsync(
        ResolvedRecipient recipient, NotificationContent content, CancellationToken ct = default)
    {
        // post to recipient.Address …
        return DeliveryResult.Succeeded();
    }
}

app.UseNotifications(n => n.AddChannel<PushChannel>());
```

## Preferences

`INotificationPreferenceProvider` returns per-recipient `NotificationPreferences`: `Enabled`,
`DoNotDisturb`, `MutedCategories`, `PreferredChannel`. Suppression is enforced by the pipeline for
every send — including when `ChannelOverride` pins a channel. `NotificationPriority.Critical` bypasses
do-not-disturb by contract; nothing bypasses `Enabled = false` or a muted category.

Implement the provider (registered scoped) to store preferences in your own tables.

> Licensing: `Pragmatic.Notifications` is PolyForm Small Business (free for small businesses).

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase example application — code
that compiles and that `Showcase.IntegrationTests` exercises — and kept identical to it by the gate: a
notification enqueued from an event handler with its content from a template, the recipient resolver, the
boundary that stores them, and the host's `UseNotifications`.
