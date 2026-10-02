# Pragmatic.Design — Telemetry

Pragmatic.Design provides built-in OpenTelemetry support across all modules. Telemetry is **embedded, not a separate module** — it uses in-box `System.Diagnostics` APIs with zero additional NuGet dependencies for runtime modules. The OTel SDK is only required in the host (via `Pragmatic.Composition.Host`).

## Quick Start

Telemetry is **auto-enabled** when using `PragmaticApp.RunAsync()`. No configuration needed:

```csharp
await PragmaticApp.RunAsync(args);
// Tracing, Metrics, Logging — all enabled by default
```

### Configuration via appsettings.json

```json
{
  "Telemetry": {
    "Enabled": true,
    "Tracing": true,
    "Metrics": true,
    "Logging": true,
    "UseOtlpExporter": false,
    "ServiceName": "my-service",
    "SamplingRatio": 0.1
  }
}
```

### Configuration via code

Telemetry options are configured internally by `PragmaticBuilder` via `PragmaticOptions.Telemetry`. The SG-generated host reads these options after the `IPragmaticBuilder` callback:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    // Module strategies (auth, storage, logging, etc.)
    app.UseMultiTenancy(mt => mt.UseHeader());
    app.UseLogging(log => log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment()));
});
// Telemetry is configured via appsettings.json "Telemetry" section
```

> Telemetry configuration is primarily via `appsettings.json`. Code overrides are available on `PragmaticOptions.Telemetry` (internal to the generated host).

## Architecture

```
┌────────────────────────────────────────────────────────────────┐
│  Pragmatic.Composition.Host                                    │
│  PragmaticTelemetry.AddPragmaticTelemetry()                   │
│  ├── WithTracing (8 ActivitySources + ASP.NET/HTTP/EF Core)   │
│  ├── WithMetrics (8 Meters + ASP.NET Core)                    │
│  └── WithLogging (ILogger → OTel bridge)                      │
└────────────────────────────┬───────────────────────────────────┘
                             │ registers
┌────────────────────────────▼───────────────────────────────────┐
│  Per-module Diagnostics (zero-cost when no listener)           │
│  ├── Pragmatic.Actions     — ActionsDiagnostics                │
│  ├── Pragmatic.Persistence — PersistenceDiagnostics            │
│  ├── Pragmatic.Events      — EventsDiagnostics                 │
│  ├── Pragmatic.Caching     — CachingDiagnostics                │
│  ├── Pragmatic.Resilience  — ResilienceDiagnostics             │
│  ├── Pragmatic.Validation  — ValidationDiagnostics             │
│  ├── Pragmatic.Logging     — LoggingDiagnostics                │
│  └── Pragmatic.I18N        — I18NDiagnostics                   │
└────────────────────────────────────────────────────────────────┘
```

## Modules & Instruments

### Pragmatic.Actions
| Instrument | Type | Description |
|---|---|---|
| `pragmatic.actions.duration` | Histogram (ms) | Action execution time |
| `pragmatic.actions.invocations` | Counter | Total action invocations |
| `pragmatic.actions.failures` | Counter | Action failures |
| `pragmatic.actions.filter_short_circuits` | Counter | Filter short-circuits |
| `pragmatic.mutations.duration` | Histogram (ms) | Mutation execution time |
| `pragmatic.mutations.invocations` | Counter | Total mutations |
| `pragmatic.mutations.failures` | Counter | Mutation failures |
| `pragmatic.mutations.validation_failures` | Counter | Mutation validation failures |

### Pragmatic.Persistence
| Instrument | Type | Description |
|---|---|---|
| `pragmatic.persistence.query_duration` | Histogram (ms) | Query execution time |
| `pragmatic.persistence.queries` | Counter | Total queries |
| `pragmatic.persistence.query_failures` | Counter | Query failures |
| `pragmatic.persistence.save_duration` | Histogram (ms) | SaveChanges time |
| `pragmatic.persistence.rows_affected` | Counter | Total rows affected |
| `pragmatic.persistence.bulk_insert_duration` | Histogram (ms) | Bulk insert time |
| `pragmatic.persistence.bulk_upsert_duration` | Histogram (ms) | Bulk upsert time |
| `pragmatic.persistence.bulk_operations` | Counter | Total bulk ops |
| `pragmatic.persistence.bulk_failures` | Counter | Bulk failures |

### Pragmatic.Events
| Instrument | Type | Description |
|---|---|---|
| `pragmatic.events.dispatch_duration` | Histogram (ms) | Event dispatch time |
| `pragmatic.events.dispatched` | Counter | Total events dispatched |
| `pragmatic.events.handler_failures` | Counter | Handler failures |

### Pragmatic.Caching
| Instrument | Type | Description |
|---|---|---|
| `pragmatic.cache.hits` | Counter | Cache hits |
| `pragmatic.cache.misses` | Counter | Cache misses |
| `pragmatic.cache.sets` | Counter | Cache set operations |
| `pragmatic.cache.invalidations` | Counter | Cache invalidations |

### Pragmatic.Resilience
| Instrument | Type | Description |
|---|---|---|
| `pragmatic.resilience.duration` | Histogram (ms) | Pipeline duration |
| `pragmatic.resilience.executions` | Counter | Pipeline executions |
| `pragmatic.resilience.retry_attempts` | Counter | Retry attempts |
| `pragmatic.resilience.circuit_rejections` | Counter | Circuit breaker rejections |
| `pragmatic.resilience.timeouts` | Counter | Timeouts |
| `pragmatic.resilience.bulkhead_rejections` | Counter | Bulkhead rejections |

### Pragmatic.Validation
| Instrument | Type | Description |
|---|---|---|
| `pragmatic.validation.duration` | Histogram (ms) | Validation time |
| `pragmatic.validation.executions` | Counter | Total validations |
| `pragmatic.validation.failures` | Counter | Validation failures |

### Pragmatic.Logging
| Instrument | Type | Description |
|---|---|---|
| `pragmatic.logging.throughput` | Counter | Log entries processed |
| `pragmatic.logging.drops` | Counter | Log entries dropped |
| `pragmatic.logging.batch_latency` | Histogram (ms) | Batch processing latency |
| `pragmatic.logging.batch_size` | Histogram | Adaptive batch size |

## Exporters

| Environment | Exporter | Configuration |
|---|---|---|
| Development | Console | Default when `UseOtlpExporter = false` |
| Production | OTLP | Set `UseOtlpExporter = true` |

OTLP endpoint is configured via standard environment variable:
```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
```

## W3C Context Propagation

### Trace Context
`CorrelationIdProvider` uses `Activity.Current.TraceId` as correlation ID when W3C `traceparent` header is present. This unifies log correlation IDs with distributed traces.

### Baggage Propagation
Add the baggage middleware to propagate user/tenant context across services:

```csharp
app.UsePragmaticBaggage(); // Propagates user.id, tenant.id to Activity.Baggage
```

Configuration:
```csharp
app.UsePragmaticBaggage(options =>
{
    options.PropagateUserId = true;      // Default: true
    options.PropagateTenantId = true;    // Default: true
    options.UserIdClaimType = "sub";     // Default: "sub"

    // Custom header-to-baggage mappings
    options.CustomBaggageHeaders.Add(new BaggageHeaderMapping
    {
        HeaderName = "X-Feature-Flag",
        BaggageKey = "feature.flag"
    });
});
```

## Sampling

| Environment | Default | Configuration |
|---|---|---|
| Development | 100% (all traces) | Always on |
| Production | 10% | `SamplingRatio = 0.1` |

Uses `ParentBasedSampler` with `TraceIdRatioBasedSampler` — child spans honor parent's sampling decision.

## Activity Tags (Semantic Conventions)

Tags follow the `pragmatic.{module}.{property}` convention. See `Pragmatic.Abstractions/Telemetry/Conventions/` for all tag constants:

- `ActionTags` — `pragmatic.action.name`, `pragmatic.action.kind`, `pragmatic.action.result`
- `CacheTags` — `pragmatic.cache.key`, `pragmatic.cache.hit`
- `DbTags` — `pragmatic.db.entity`, `pragmatic.db.operation`
- `ErrorTags` — `pragmatic.error.code`, `pragmatic.error.type`
- `EventTags` — `pragmatic.event.name`, `pragmatic.event.handler`
- `I18NTags` — `pragmatic.i18n.culture`, `pragmatic.i18n.source`
- `ResilienceTags` — `pragmatic.resilience.policy`, `pragmatic.resilience.outcome`

## Zero-Cost Design

All modules use `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter` which are **zero-cost when no listener is registered**. No OTel NuGet packages are needed in runtime modules — only `Pragmatic.Composition.Host` references the OTel SDK.
