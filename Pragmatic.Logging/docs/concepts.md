# Architecture and Core Concepts

This guide explains **why** Pragmatic.Logging exists, how its pieces fit together, and how to choose the right configuration for each situation. Read this before diving into the individual feature guides.

---

## The Problem

Logging sits on the hot path of every request. A production application needs structured log output, correlation IDs for distributed tracing, PII redaction for compliance, rate limiting to prevent log spam, and multiple output targets -- all without adding measurable latency. The standard .NET logging infrastructure provides the foundation, but leaves these concerns to the developer.

### Manual formatting wastes allocations

```csharp
// Every call allocates a new string, even when the level is disabled
logger.LogInformation($"Order {orderId} placed by {customerId} for {total:C}");

// Even the recommended structured approach allocates parameter arrays
logger.LogInformation("Order {OrderId} placed by {CustomerId} for {Total}",
    orderId, customerId, total);
```

Under high throughput, string allocations from logging alone can create GC pressure that causes visible latency spikes. The standard `LoggerMessage.Define` pattern helps, but requires boilerplate for every message.

### No built-in privacy protection

```csharp
// This logs the full connection string, including the password
logger.LogError("Failed to connect: {ConnectionString}", connectionString);

// This leaks the JWT token into your log aggregator
logger.LogDebug("Auth header: {AuthHeader}", request.Headers.Authorization);

// This writes PII to disk in a GDPR-regulated environment
logger.LogInformation("User {Email} placed order {OrderId}", user.Email, orderId);
```

There is no standard mechanism to detect and redact secrets, API keys, or personally identifiable information before they reach the log output. Developers must remember to sanitize every call site -- and they will forget.

### No structured context enrichment

```csharp
// Without enrichment, you pass context manually at every call site
logger.LogInformation("Order placed. CorrelationId={CorrelationId}, UserId={UserId}, " +
    "Machine={Machine}, RequestPath={Path}",
    correlationId, userId, Environment.MachineName, httpContext.Request.Path);
```

When debugging a production incident across dozens of service replicas, you need every log entry to carry ambient context -- correlation IDs, user identifiers, request paths, machine names. Passing these values explicitly at every call site is error-prone and clutters the business code.

### No unified configuration

Each concern -- output targets, formatting, filtering, rate limiting, compliance -- requires its own configuration surface. Developers cobble together Serilog enrichers, NLog targets, custom middleware, and manual redaction code. There is no single place to configure "production-ready logging" with one call.

---

## The Solution

Pragmatic.Logging wraps the standard `Microsoft.Extensions.Logging` infrastructure with a fluent `PragmaticLoggingBuilder` API. It provides:

- **Zero-allocation formatter** -- `ZeroAllocMessageFormatter.TryFormat` uses stack-allocated buffers and pooled `StringBuilder` instances to format with no heap allocation (the formatter itself; end-to-end pipeline costs are in the performance guide)
- **Automatic secret detection and PII redaction** -- pre-compiled regex patterns scan log content before it reaches any output provider
- **Context enrichment** -- ambient properties (correlation IDs, user identity, machine name) are attached to every log entry without changes to business code
- **Multiple providers** -- console, file, JSON, NDJSON, memory, debug, Windows Event Log, and custom providers
- **Configuration presets** -- one-call setup for Development, Production, High Performance, and Compliance scenarios
- **Rate limiting** -- lock-free token bucket, sliding window, and fixed window strategies prevent log spam
- **Audit trail** -- compliance-grade recording of every redaction event and sensitive data access
- **Standard interface** -- everything works through `ILogger<T>`. No proprietary abstractions.

```csharp
// With Pragmatic: one builder, all concerns configured
builder.Services.AddPragmaticLogging(pragmatic => pragmatic
    .UseProductionPreset()           // Sensible defaults for production
    .AddConsole()                    // Colored terminal output
    .AddFile("logs/app-{Date}.log") // Rolling file with retention
    .AddJson("logs/app.json"));     // Structured JSON for log aggregators
```

Or integrated with the Pragmatic.Composition host:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
        log.AddFile("logs/app-{Date}.log");
    });
});
```

### Performance

Benchmarked on .NET 10 against Serilog, NLog and ZLogger, each writing into a sink that renders the
message and reads every property (per call, 2026-10-05):

| Scenario | Pragmatic | ZLogger | NLog | Serilog |
|----------|-----------|---------|------|---------|
| `[LoggerMessage]` call site | 201.6 ns / 544 B | 149.1 ns / 192 B | 382.8 ns / 1,416 B | 302.2 ns / 800 B |
| Simple logging | 214.5 ns / 592 B | 164.7 ns / 216 B | 220.5 ns / 760 B | 259.5 ns / 528 B |
| Structured (with scope) | 517.5 ns / 1,176 B | 432.6 ns / 520 B | 671.5 ns / 1,328 B | 813.2 ns / 1,912 B |
| Exception logging | 233.2 ns / 608 B | 175.7 ns / 232 B | 240.2 ns / 776 B | 268.5 ns / 528 B |
| Production (context + scope) | 747.5 ns / 2,368 B | 519.8 ns / 832 B | 968.0 ns / 2,872 B | 1,007.9 ns / 3,000 B |

Pragmatic is ahead of Serilog and NLog in every scenario, the production preset with context enrichment
included; ZLogger is ahead of all three throughout.

The **deferred pipeline** (the typed state handed to the sink with no `LogEntry` and no eager
rendering) applies only to a provider that declares `SupportsDeferredWrite`, and the only one that does
today is `PragmaticNullProvider`, which discards the entry. A provider that writes anywhere takes the
materialized pipeline measured above. See [BENCHMARK-RESULTS.md](../BENCHMARK-RESULTS.md) for the run,
the machine and the reports.

---

## How It Works

Pragmatic.Logging is built on three layers: the **builder** that configures the pipeline, the **provider base** that enforces the pipeline for every output target, and the **providers** that write log entries to their destinations.

### Layer 1: PragmaticLoggingBuilder

The `PragmaticLoggingBuilder` is the entry point for all configuration. `services.AddPragmaticLogging(pragmatic => …)` (or `app.UseLogging(…)` in a Pragmatic host) hands it to you, and it provides a fluent API for adding providers, applying presets, enabling privacy features, and configuring performance options. There is one: `Pragmatic.Logging.Extensions.PragmaticLoggingBuilder`.

```csharp
// inside services.AddPragmaticLogging(pragmatic => { … })

// Presets configure all knobs at once
pragmatic.UseProductionPreset();

// Then override individual settings
pragmatic.Configure(opts => opts.MinimumLevel = LogLevel.Debug);

// Add providers
pragmatic.AddConsole();
pragmatic.AddFile("logs/app-{Date}.log");

// Enable privacy features
pragmatic.EnableDataRedaction(r => r.RedactionPlaceholder = "[REDACTED]");
pragmatic.EnableAuditTrail(a => a.StorageType = "FileSystem");
```

### Layer 2: PragmaticLoggerProviderBase

Every provider -- built-in or custom -- extends `PragmaticLoggerProviderBase`. This base class enforces a consistent pipeline for every log entry:

```
Log Entry
  |
  v
[IsEnabled check] --no--> (dropped, zero work)
  |
  yes
  v
[Advanced filter chain] --filtered--> (dropped, counter incremented)
  |
  pass
  v
[Context enrichment] -- adds CorrelationId, UserId, MachineName, etc.
  |
  v
[Structured property filter] -- applies ContextFilterMode
  |
  v
[Data redaction]
  |-- SecretDetector scans message + property values
  |-- PragmaticDataRedactor checks property names + patterns
  |-- Audit trail records each redaction event
  |
  v
[WriteLogCore] -- provider-specific output (console, file, JSON, etc.)
```

This means every provider automatically benefits from the full privacy stack, context enrichment, and filtering. You never need to implement redaction in a custom provider.

### Layer 3: Providers

Each provider implements `WriteLogCore` and handles the specific output format and destination. Pragmatic.Logging ships with eight built-in providers:

| Provider | Class | Output |
|----------|-------|--------|
| Console | `PragmaticConsoleProvider` | Colored terminal output with structured data |
| File | `PragmaticFileProvider` | File-based with rolling, retention, async I/O |
| JSON | `PragmaticJsonProvider` | Structured JSON for log aggregators |
| Enhanced JSON | `PragmaticEnhancedJsonProvider` | NDJSON with async buffering |
| Memory | `PragmaticMemoryProvider` | In-memory buffer for testing |
| Debug | `PragmaticDebugProvider` | Debug output window (Visual Studio, Rider) |
| Null | `PragmaticNullProvider` | No-op for benchmarking |
| Windows Event Log | `PragmaticWindowsEventLogProvider` | Windows Event Log |

---

## High-Performance Logging: [LoggerMessage]

For hot paths, declare `[LoggerMessage]` methods. Pragmatic.Logging works entirely through the
standard `ILogger` abstraction, and the attribute is the one you know: in a project that references
`Pragmatic.SourceGenerator` it binds to Pragmatic's, whose generated state writes itself as UTF-8 and
masks a `[PersonalData]` or `[NotLogged]` argument at the call site. Without the generator it is
Microsoft's. See [Log call sites](call-sites.md).

```csharp
using Microsoft.Extensions.Logging;

public partial class OrderService(ILogger<OrderService> logger)
{
    public void PlaceOrder(Guid orderId, string customerId)
    {
        LogOrderPlaced(orderId, customerId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed by {CustomerId}")]
    private partial void LogOrderPlaced(Guid orderId, string customerId);
}
```

The generated method performs an `IsEnabled` check before any work and hands the logger a struct
state with no boxing. When the Pragmatic JSON provider writes it from that state — context enrichment,
filters and pattern redaction off, no active scope — a call like this one allocates nothing; the
[conditions](call-sites.md#how-a-provider-writes-it) are listed with the reasons.

### When to use which approach

| Approach | Allocation | Use Case |
|----------|-----------|----------|
| `[LoggerMessage]` partial method | Minimal (no boxing) | Hot paths, high-throughput code |
| `logger.LogInformation("...", args)` | Low (parameter array) | Business logic, moderate throughput |
| String interpolation `$"..."` | High (string + boxing) | Never in production code |

---

## Privacy and Redaction

Pragmatic.Logging provides a layered defense against accidental exposure of sensitive data.

### Secret Detection

The `SecretDetector` scans log messages and property values against a curated library of pre-compiled regex patterns. Each detection carries a confidence score and severity level.

| Category | Examples | Severity |
|----------|----------|----------|
| Crypto | RSA/EC/PGP private keys | Critical |
| API Keys | AWS (`AKIA...`), GitHub PAT (`ghp_...`), Stripe (`sk_live_...`) | Critical |
| Tokens | JWT (`eyJ...`), Bearer tokens, OAuth refresh tokens | High |
| Database | Connection strings with `Password=`, MongoDB URIs | Critical |
| Cloud | Azure Storage Key, GCP Service Account Key | Critical |
| Application | Encryption keys, hash salts | Medium |

The `SecretDetectionOptions.MinimumConfidenceForRedaction` threshold (default: 0.7) controls which detections trigger automatic redaction. Detections below the threshold are logged for review but not redacted.

### Data Redaction

Beyond secret detection, the `PragmaticDataRedactor` handles PII and business-sensitive data based on property names and message content patterns:

1. **Explicit flag** -- Properties marked with `PropertyCharacteristics.Redacted` are always redacted
2. **Name matching** -- Property names checked against a `HashSet<string>` of sensitive names (case-insensitive) and compiled regex patterns
3. **Message scanning** -- Log message text scanned for emails, credit card numbers, SSNs, and custom patterns

```csharp
pragmatic.EnableDataRedaction(redaction =>
{
    redaction.RedactionPlaceholder = "[REDACTED]";
    redaction.SensitivePropertyNames = ["password", "apiKey", "email", "ssn"];
    redaction.PropertyNamePatterns = [@".*password.*", @".*secret.*"];
    redaction.MessageRedactionPatterns = [@"[\w.+-]+@[\w-]+\.[\w.]+"];  // Email
});
```

### Redaction Styles

| Style | Output | Description |
|-------|--------|-------------|
| `Placeholder` | `[API_KEY_REDACTED]` | Descriptive placeholder with secret type |
| `PreserveLength` | `********************************` | Asterisks matching original length |
| `PreserveStructure` | `eyJ***.***.***` | Keeps format markers (JWT segments) |
| `Minimal` | `[REDACTED]` | Simple constant replacement |

### Compliance Standards

Pre-built compliance templates configure the correct sensitive property lists, message patterns, redaction placeholders, audit requirements, and data retention periods:

| Standard | Placeholder | Deep Redaction | Audit Trail | Retention |
|----------|-------------|---------------|-------------|-----------|
| **GDPR** | `[GDPR-REDACTED]` | Yes | Yes | 2 years |
| **HIPAA** | `[PHI-REDACTED]` | Yes | Yes | 6 years |
| **PCI-DSS** | `[CARD-DATA-REDACTED]` | Yes | Yes | 1 year |
| **CCPA** | `[REDACTED]` | Yes | Yes | 90 days |
| **SOX** | `[REDACTED]` | Yes | Yes | 7 years |

```csharp
// GDPR-compliant logging
pragmatic.UseCompliancePreset(ComplianceStandard.Gdpr);

// Multi-compliance (uses most restrictive settings)
var config = ComplianceTemplates.CreateMultiCompliance(
    includeGdpr: true, includeHipaa: true, includePciDss: false);
```

---

## Zero-Allocation Performance

For hot paths, Pragmatic.Logging provides infrastructure that eliminates heap allocations.

### StackAllocatedBuffer

Small messages (up to 1,024 characters) are formatted into a `stackalloc` buffer. Larger messages fall back to `ArrayPool<char>.Shared` so the rented array is reused across calls.

### ZeroAllocMessageFormatter

The core formatting engine uses a `ThreadLocal<StringBuilder>` pool and `ArrayPool<object?>` for parameter arrays, minimizing allocations on every call. It has specialized handlers for common types:

| Type | Allocation | Method |
|------|-----------|--------|
| `int`, `long`, `float`, `double`, `decimal` | Zero | `TryFormat(Span<char>)` |
| `DateTime`, `DateTimeOffset` | Zero | `TryFormat` with ISO 8601 |
| `TimeSpan`, `Guid` | Zero | `TryFormat` |
| `bool` | Zero | Literal copy |
| `string` | Zero | `AsSpan().CopyTo()` |
| `ISpanFormattable` | Zero | Interface `TryFormat` |
| Other | 1 allocation | `ToString()` fallback |

### Object Pooling

| Pool | Purpose |
|------|---------|

### Early-Exit IsEnabled Checks

Every provider performs an `IsEnabled` check with `[MethodImpl(AggressiveInlining)]` before any formatting work begins. If the log level is below the configured minimum, the call returns immediately with zero work done.

---

## Context Enrichment

Context enrichment automatically attaches ambient information to every log entry without requiring the developer to pass values explicitly at every call site.

### Built-in Context Providers

| Provider | Priority | Scope | Key Properties |
|----------|----------|-------|---------------|
| `HttpContextProvider` | 100 | Per-request | RequestPath, RequestMethod, UserId, RemoteIpAddress |
| `CorrelationIdProvider` | 50 | Per-request | CorrelationId, TraceId, SpanId |
| `ThreadContextProvider` | 500 | Per-call | ThreadId, ThreadName, IsBackground |
| `ProcessContextProvider` | 900 | Per-process | ProcessId, ProcessName, AppVersion |
| `MachineContextProvider` | 1000 | Per-machine | MachineName, OSVersion, CLRVersion |

### Custom Providers

Extend `ContextProviderBase` for domain-specific context. The provider lives as long as the context
manager (a singleton), so what belongs to the request, the tenant, it reads on each call, not in its
constructor, and it declares itself not static so that the manager asks it on each call:

```csharp
public sealed class TenantContextProvider(IHttpContextAccessor httpContextAccessor)
    : ContextProviderBase("Tenant", priority: 80)
{
    public override bool IsStatic => false;

    public override bool IsAvailable() => CurrentTenant() is { IsResolved: true };

    public override IReadOnlyDictionary<string, object?> GetContextProperties()
    {
        var tenant = CurrentTenant();
        return CreatePropertiesDictionary(("TenantId", tenant?.TenantId), ("TenantName", tenant?.TenantName));
    }

    private ITenantContext? CurrentTenant()
        => httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();
}
```

See [Context Enrichment](context-enrichment.md#4-creating-a-custom-provider) for its registration.

### ASP.NET Core Middleware

Two middleware components wire enrichment into the HTTP pipeline:

- **`LoggingEnrichmentMiddleware`** -- Creates a logging scope with correlation ID, request path, request method, and user identity. Optionally logs request start/completion with timing and maps HTTP status codes to log levels (5xx = Error, 4xx = Warning, 2xx/3xx = Information).

- **`BaggagePropagationMiddleware`** -- Propagates selected context values into `Activity.Baggage` for automatic forwarding to downstream services via W3C Baggage headers.

```csharp
app.UseMiddleware<BaggagePropagationMiddleware>();
app.UseMiddleware<LoggingEnrichmentMiddleware>();
```

### Per-Provider Context Filtering

Each provider can independently control which context properties it includes via `ContextFilterConfiguration`. This lets you add full context to file logs while keeping console output compact.

```csharp
logging.AddConsole(config =>
{
    config.ContextFilter = new ContextFilterConfiguration
    {
        Mode = ContextFilterMode.Include,
        PropertyNames = new HashSet<string> { "CorrelationId", "UserId" }
    };
});
```

| Mode | Behavior |
|------|----------|
| `All` | Include all context properties (default) |
| `Include` | Include only listed properties |
| `Exclude` | Include all except listed properties |
| `None` | Include no context properties |

---

## Configuration Presets

`PragmaticLoggingBuilder` provides four built-in presets that configure all knobs at once.

### Development

```csharp
pragmatic.UseDevelopmentPreset();
```
- MinimumLevel: `Trace`
- HighPerformanceMode: disabled
- Telemetry: disabled
- Machine context: enabled, user context: disabled

### Production

```csharp
pragmatic.UseProductionPreset();
```
- MinimumLevel: `Information`
- Background processing: enabled, buffer size 5000
- Rate limiting: enabled (1000 msg/sec)
- Correlation IDs and user context: enabled

### High Performance

```csharp
pragmatic.UseHighPerformancePreset();
```
- MinimumLevel: `Information` (skips Debug/Trace)
- HighPerformanceMode: enabled
- Zero-allocation optimizations: enabled
- Buffer size: 10000, flush threshold: 1000

### Compliance

```csharp
pragmatic.UseCompliancePreset(ComplianceStandard.Gdpr);
```
- Redaction: enabled
- Secret detection: enabled
- Audit trail: enabled with file-system storage (database storage is not implemented)
- Confidence thresholds vary by standard (GDPR: 0.8, HIPAA: 0.9, PCI-DSS: 0.95)

### Override After Preset

Presets set sensible defaults. Override individual settings after applying a preset:

```csharp
pragmatic
    .UseProductionPreset()
    .Configure(opts => opts.MinimumLevel = LogLevel.Debug);
```

---

## Rate Limiting

Under sustained load, a noisy log category can produce thousands of duplicate messages per second. The `HighPerformanceRateLimiter` throttles log output using one of three strategies without blocking the caller.

| Strategy | Algorithm | Behavior |
|----------|-----------|----------|
| `TokenBucket` | Token bucket | Allows bursts up to bucket size, then rate-limits. Best for most scenarios. |
| `SlidingWindow` | Sliding window | Precise rate over a moving window. Best for strict compliance. |
| `FixedWindow` | Fixed window | Simple counter reset at boundary. Fastest, but allows edge bursts. |

```csharp
pragmatic.EnableRateLimiting(rl =>
{
    rl.MaxMessagesPerSecond = 1000;
    rl.BurstSize = 100;
    rl.Strategy = "TokenBucket";
});
```

---

## Background Processing and Batching

Synchronous writes to disk or network block the calling thread. Pragmatic.Logging decouples log production from log output.

### Background Processing

When enabled, log entries are enqueued into a bounded channel. A background consumer thread drains the queue and writes entries in batches.

```
Caller thread              Background thread
     |                           |
     |-- Enqueue(entry) -->      |
     |   (non-blocking)          |
     |                      [Wait for batch/timer]
     |                           |
     |                      [WriteLogCore(batch)]
```

### Queue Overflow Strategies

| Strategy | Data Loss | Blocking |
|----------|-----------|----------|
| `DropOldest` | Oldest entries | No |
| `DropNewest` | Newest entries | No |
| `Block` | None | Yes |
| `Expand` | None | No |

For production, `DropOldest` is recommended. Use `Block` only in compliance-critical scenarios.

### Batching

Individual disk writes are expensive. Batching amortizes the cost by collecting entries until either the `BatchSize` threshold or `FlushTimeout` is reached.

| Scenario | Recommended Batch Size | Flush Interval |
|----------|----------------------|----------------|
| Web API (moderate traffic) | 50-100 | 1-2 seconds |
| High-throughput service | 200-500 | 5-10 seconds |
| Real-time streaming | 10-20 | 100 ms |
| Compliance (every entry matters) | 25-50 | 500 ms |

---

## Audit Trail

The `PragmaticAuditService` records every redaction event, sensitive data access, and compliance violation into a durable audit store. It runs as an `IHostedService` with batch processing and configurable flush intervals.

### Storage Options

| Storage | Class | Persistence | Best For |
|---------|-------|-------------|----------|
| Memory | `MemoryAuditStorage` | In-process | Testing, development |
| File System | `FileSystemAuditStorage` | Disk | Single-server deployments |

### Audit Policies

| Policy | Description |
|--------|-------------|
| Default | Records all redaction and access events |
| GDPR | Records everything, forces immediate flush for violations |
| HighPerformance | Only records high-severity events |
| Development | Minimal auditing for fast iteration |

```csharp
pragmatic.EnableAuditTrail(audit =>
{
    audit.StorageType = "FileSystem";
    audit.PolicyType = "GDPR";
    audit.BatchSize = 100;
    audit.FlushIntervalSeconds = 30;
});
```

---

## Bootstrap Logger

For logging before the DI container is built (during startup configuration):

```csharp
using var bootstrap = BootstrapLogger.Create();
bootstrap.LogInformation("Starting application...");

var builder = WebApplication.CreateBuilder(args);
// ... configure services ...
```

---

## Configuration via appsettings.json

All options bind to the `PragmaticLogging` configuration section:

```json
{
  "PragmaticLogging": {
    "Enabled": true,
    "MinimumLevel": "Information",
    "HighPerformanceMode": false,
    "Privacy": {
      "EnableRedaction": true,
      "EnableSecretDetection": true,
      "ComplianceStandard": "General",
      "SecretDetection": {
        "MinimumSecretLength": 8,
        "PrecompilePatterns": true,
        "MinimumConfidenceForRedaction": 0.7
      },
      "DataRedaction": {
        "RedactionPlaceholder": "[REDACTED]",
        "SensitivePropertyNames": ["password", "secret"]
      }
    },
    "Performance": {
      "BufferSize": 1000,
      "FlushThreshold": 100,
      "EnableZeroAllocation": true,
      "UseBackgroundProcessing": true
    },
    "RateLimiting": {
      "Enabled": false,
      "MaxMessagesPerSecond": 1000,
      "Strategy": "TokenBucket"
    },
    "Providers": {
      "Console": { "Enabled": true, "UseColors": true },
      "File": { "Enabled": true, "BasePath": "./logs", "MaxFileSizeMB": 100 }
    }
  }
}
```

---

## Ecosystem Integration

### IPragmaticBuilder

Pragmatic.Logging integrates with the Pragmatic.Composition host via `UseLogging()`:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
        log.AddFile("logs/app-{Date}.log");
    });
});
```

### Standard ILogger

After registration, inject and use `ILogger<T>` as usual. Pragmatic.Logging handles enrichment, redaction, and routing behind the scenes:

```csharp
public class OrderService(ILogger<OrderService> logger)
{
    public void ProcessOrder(int orderId)
    {
        logger.LogInformation("Processing order {OrderId}", orderId);
    }
}
```

### Custom Providers

Implement `IPragmaticLoggerProvider` or extend `PragmaticLoggerProviderBase` for custom output targets. The base class handles configuration, metrics, health checks, enrichment, and redaction -- you only implement `WriteLogCore`.

```csharp
public sealed class SlackAlertProvider : PragmaticLoggerProviderBase
{
    protected override void WriteLogCore(LogEntry logEntry)
    {
        if (logEntry.LogLevel < LogLevel.Error) return;
        // Send to Slack webhook
    }
}
```

---

## Key Types

| Type | Namespace | Purpose |
|------|-----------|---------|
| `PragmaticLoggingBuilder` | `Pragmatic.Logging` | Fluent configuration API |
| `PragmaticLoggingOptions` | `Pragmatic.Logging.Configuration` | Main options class |
| `SecretDetector` | `Pragmatic.Logging.Privacy` | Secret detection engine |
| `PragmaticDataRedactor` | `Pragmatic.Logging.Privacy` | Data redaction service |
| `PragmaticAuditService` | `Pragmatic.Logging.Privacy.Audit` | Audit trail service |
| `PragmaticFileProvider` | `Pragmatic.Logging.Providers` | High-performance file provider |
| `PragmaticConsoleProvider` | `Pragmatic.Logging.Providers` | Console provider |
| `PragmaticJsonProvider` | `Pragmatic.Logging.Providers` | JSON/NDJSON provider |
| `PragmaticMemoryProvider` | `Pragmatic.Logging.Providers` | In-memory provider for testing |
| `LoggingEnrichmentMiddleware` | `Pragmatic.Logging.AspNetCore` | ASP.NET Core enrichment middleware |
| `BaggagePropagationMiddleware` | `Pragmatic.Logging.AspNetCore` | Distributed tracing baggage propagation |
| `BootstrapLogger` | `Pragmatic.Logging.AspNetCore` | Pre-DI logger |
| `ContextManager` | `Pragmatic.Logging.Context` | Context provider registry |
| `HighPerformanceRateLimiter` | `Pragmatic.Logging` | Lock-free rate limiter |
| `ZeroAllocMessageFormatter` | `Pragmatic.Logging.ZeroAllocation` | Zero-allocation message formatting |

---

## See Also

- [Getting Started](getting-started.md) -- Installation, registration, first provider
- [Log Providers](providers.md) -- Detailed configuration for each provider
- [Privacy and Security](privacy-security.md) -- Secret detection, data redaction, compliance
- [Performance](performance.md) -- Zero-allocation, batching, rate limiting, background processing
- [Context Enrichment](context-enrichment.md) -- Built-in and custom context providers
- [Common Mistakes](common-mistakes.md) -- Pitfalls and how to avoid them
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide
