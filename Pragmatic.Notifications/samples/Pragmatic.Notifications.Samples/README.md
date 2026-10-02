# Pragmatic.Notifications.Samples

Runnable samples for [Pragmatic.Notifications](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Notifications/samples/Pragmatic.Notifications.Samples/Pragmatic.Notifications.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

Harness-based (zero infrastructure):

- `BasicSendSample`
- `RecipientResolutionSample`
- `AudienceAndPrioritySample`
- `CategoryAndTrackingSample`
- `EnqueuedVsSyncSample`

Real-pipeline (build a DI container / host; in-memory transports, no external infrastructure):

- `CustomChannelSample` — custom `INotificationChannel`
- `MultiChannelConfigurationSample` — `NotificationsBuilder` multi-channel + priority fan-out
- `ChannelOverrideSample` — `NotificationRequest.ChannelOverride`
- `CustomRecipientResolverSample` — custom `IRecipientResolver` (UserId → email)
- `CustomPreferenceProviderSample` — custom `INotificationPreferenceProvider` (category muting)
- `SmtpChannelSample` — Email/SMTP channel contract (fake sender; real `.AddSmtp(...)` wiring shown in comments)
- `WebhookChannelSample` — real `WebhookChannel` HTTP POST to a local sink + SSRF allowlist
- `EfCoreStoreSample` — `EfCoreNotificationStore` over SQLite (`.UseEfCoreStore(...)`)
- `BackgroundDeliverySample` — `NotificationDeliveryWorker` background `EnqueueAsync` processing
- `DiagnosticsMetricsSample` — `NotificationsDiagnostics` OTel metrics via `MeterListener`

## Related

- Module: [Pragmatic.Notifications](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/notifications/
- Source: `Pragmatic.Notifications/src/Pragmatic.Notifications/`
