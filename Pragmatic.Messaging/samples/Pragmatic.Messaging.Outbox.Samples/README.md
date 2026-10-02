# Pragmatic.Messaging.Outbox.Samples

Runnable console samples for Pragmatic.Messaging. Each `{Feature}Sample.cs` has a
`Run`/`RunAsync` that prints a header and a real demonstration; `Program.cs` runs
them in sequence. Most run entirely in-process (in-memory bus + opt-in stores);
the outbox and EF Core stores run against SQLite in-memory.

## Run

```bash
dotnet run --project Pragmatic.Messaging/samples/Pragmatic.Messaging.Outbox.Samples
```

## Samples

| File | Shows |
|------|-------|
| `MiddlewareSample` | Custom `IMessageMiddleware` — wrap order + short-circuit |
| `DeadLetterSample` | `IDeadLetterStore` / `DeadLetterMessage` — poison capture + replay |
| `IdempotencySample` | `EnableIdempotency` + `IIdempotencyStore` — dedup on message id |
| `ScheduledMessagesSample` | `IMessageScheduler` contract — delayed delivery (timer-backed) |
| `AuditingSample` | `AddMessageAuditing` + `IAuditStore` + `AuditQuery` |
| `TestingHarnessSample` | `MessageBusTestHarness` — record + assert published messages |
| `ResilienceSample` | SG-generated `ResiliencePipeline` from `[MessageHandler]`+`[Retry]` |
| `MultiBusSample` | `[OnBus]` + `NamedBusMessageBus` routing by `bus.name` (preview) |
| `EfCoreStoresSample` | `EfCoreIdempotencyStore` / `EfCoreAuditStore` (SQLite) |
| `KafkaTransportSample` | `UseKafka` wiring — setup-only (needs a broker) |
| `OutboxSample` | Transactional outbox: interceptor → delivery service (SQLite) |

## Notes

- **Scheduled messages**: the core package ships the `IMessageScheduler`
  abstraction only; the sample provides a minimal timer-backed implementation.
  Durable scheduling comes from an infrastructure package (e.g. Messaging.Jobs).
- **Multi-bus** is a preview API (`PRAGMSG_MULTIBUS`); routing logic is live but
  automatic named-bus DI materialisation is not finalised, so the sample composes
  `NamedBusMessageBus` by hand.
- **Kafka** is broker-dependent and therefore not executed — the sample documents
  the wiring shape only.
