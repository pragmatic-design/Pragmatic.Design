---
title: "Log Providers"
description: "Pragmatic.Logging ships with a set of built-in log providers that cover the most common output targets. Each provider extends `PragmaticLoggerProviderBase`, inh"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Logging/docs/providers.md
sidebar:
  order: 6
---
Pragmatic.Logging ships with a set of built-in log providers that cover the most common output targets. Each provider extends `PragmaticLoggerProviderBase`, inheriting automatic metrics collection, health checks, context enrichment, privacy redaction, and configuration hot-reload. You pick the providers you need and compose them through the fluent `PragmaticLoggingBuilder`.

---

## Quick Start

All providers are registered through the `AddPragmaticLogging` extension method on `IServiceCollection`. The callback receives a `PragmaticLoggingBuilder` that exposes a fluent API for adding providers.

```csharp
services.AddPragmaticLogging(logging => logging
    .AddConsole(o => o.UseColors = true)
    .AddFile("logs/app-{Date}.log", o => o.CustomProperties["MaxRetainedFiles"] = 30)
    .AddJson("logs/structured.json")
);
```

---

## 1. Console -- `PragmaticConsoleProvider`

Terminal output is the first thing developers reach for when debugging a running application. The Console provider writes colored, structured output to `stdout` with automatic detection of ANSI color support, Windows Console API fallback, and plain-text mode for CI/CD environments.

### Key capabilities

- Automatic terminal capability detection (ANSI, Windows Console API, no-color).
- Custom `ColorScheme` per log level (Information, Warning, Error, Critical).
- Structured property rendering inline with the message.
- Category name truncation for compact output.
- Thread-safe writes with a shared `StringBuilder` pool.

### Configuration

```csharp
logging.AddConsole(PragmaticConsoleConfiguration.ForAdvancedConsole());

// Or with inline options
logging.AddConsole(config =>
{
    config.MinimumLevel = LogLevel.Debug;
    config.Formatting.TimestampFormat = "HH:mm:ss.fff";
    config.Formatting.UseUtcTimestamp = false;
    config.IncludeStructuredProperties = true;
});
```

### Console-specific options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `UseColors` | `bool` | `true` | Enable colored output |
| `IncludeStructuredData` | `bool` | `true` | Render structured properties inline |
| `OutputFormat` | `string` | `"Structured"` | Output format style |

### Example output

```
[14:32:15.123] [INFO] OrderService: Order placed successfully {OrderId: 42, CustomerId: "C-100"}
[14:32:15.456] [WARN] PaymentService: Payment retry initiated {Attempt: 2, Amount: 99.50}
```

---

## 2. File -- `PragmaticFileProvider`

Production applications need durable, rotated log files that do not consume unbounded disk space. The File provider uses channel-based async I/O with lock-free concurrent writes, automatic rolling by size and time, retention policies, and exponential backoff retry on write failures.

### Key capabilities

- Async channel-based processing with bounded queues and back-pressure.
- Template-based file naming: `app-{Date}.log` produces `app-2025-03-22.log`.
- Rolling intervals: hourly, daily, weekly, monthly, yearly.
- Size-based rolling with configurable thresholds.
- Retention by file count: on each roll, files beyond `MaxRetainedFiles` are deleted, oldest first.
- 8 KB buffered streams for optimal disk throughput.

### Configuration

```csharp
logging.AddFile("logs/app-{Date}.log");

// With detailed options
logging.AddFile("logs/app-{Date}.log", config =>
{
    config.MinimumLevel = LogLevel.Information;
    config.Performance.EnableBatching = true;
    config.Performance.BatchSize = 100;
    config.Performance.FlushInterval = TimeSpan.FromSeconds(5);
    config.Performance.MaxQueueSize = 50000;
    config.Formatting.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    config.Formatting.IncludeExceptionDetails = true;
});
```

### File-specific options

Set in `config.CustomProperties`; the defaults are those of `PragmaticProviderConfiguration.ForFile()`.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `MaxFileSize` | `long` | `10 MB` | Size in bytes at which the current file rolls |
| `RollingInterval` | `string` | `"Day"` | `Hour`, `Day`, `Week`, `Month`, `Year` or `None` |
| `MaxRetainedFiles` | `int` | `31` | Files kept after a roll; `0` keeps every file |
| `FlushAfterWrite` | `bool` | `true` | Flush after each entry; `false` batches the flushes |

The file name is the path given to `AddFile`, with `{Date}`, `{DateTime}`, `{Year}`, `{Month}`,
`{Day}` and `{Hour}` expanded. Retention finds the provider's own files by that template, placeholders
as wildcards, so a second provider writing to the same folder under another name is never touched.

---

## 3. JSON -- `PragmaticJsonProvider`

Log aggregation systems such as the ELK Stack, Fluentd, or Azure Monitor consume structured JSON. The JSON provider outputs each log entry as a JSON object with `@timestamp`, `@level`, `@logger`, `@message`, and `@properties` fields, using `Utf8JsonWriter` for high-performance serialization.

### Key capabilities

- High-performance `Utf8JsonWriter` serialization.
- Automatic file rolling by size and date.
- Pretty-print mode for development readability.
- Complex object serialization with `System.Text.Json`.
- Both console and file output targets.

### Configuration

```csharp
logging.AddJson("logs/structured.json", PragmaticJsonConfiguration.ForHighPerformanceJsonFile());

// Pretty-printed for development
logging.AddJson("logs/dev.json", PragmaticJsonConfiguration.ForPrettyJson());
```

### JSON-specific custom properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `PrettyPrint` | `bool` | `false` | Indent JSON output |
| `SerializeComplexObjects` | `bool` | `true` | Serialize nested objects as JSON |
| `SkipValidation` | `bool` | `true` | Skip JSON writer validation for speed |
| `AutoFlush` | `bool` | `false` | Flush after each entry |
| `MaxFileSizeBytes` | `long` | `500 MB` | File size before rolling |
| `RollByDate` | `bool` | `true` | Roll files daily |

### Example output (compact)

```json
{"@timestamp":"2025-03-22T10:30:00.000Z","@level":"INFO","@logger":"OrderService","@message":"Order processed","@properties":{"OrderId":42,"Total":199.99}}
```

---

## 4. Enhanced JSON -- `PragmaticEnhancedJsonProvider`

High-throughput systems that forward logs to streaming pipelines need NDJSON (newline-delimited JSON) with async buffering and atomic file writes. The Enhanced JSON provider extends the JSON provider with NDJSON format, async buffering via `ConcurrentQueue`, timer-based flush, and optional atomic writes using temp-file-then-rename.

### Key capabilities

- NDJSON output (one JSON object per line) for streaming compatibility.
- Async buffering with configurable flush intervals and batch sizes.
- Atomic writes for crash-safe file operations.
- Flattened properties for better log search indexing.
- Sequential or timestamp-based roll strategies.
- Hourly rolling option in addition to daily.

### Configuration

```csharp
logging.AddNdjsonAsync("logs/events.ndjson");

// High-performance file output
logging.AddNdjsonAsync("logs/events.ndjson", PragmaticEnhancedJsonConfiguration.ForHighPerformanceFile());

// Real-time streaming with minimal buffering
logging.AddNdjsonAsync("logs/stream.ndjson", PragmaticEnhancedJsonConfiguration.ForRealTimeStreaming());
```

### Enhanced JSON custom properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableNDJSON` | `bool` | `true` | Use NDJSON format |
| `EnableAsyncBuffering` | `bool` | `true` | Queue entries for async processing |
| `EnableAtomicWrites` | `bool` | `true` | Use temp file + rename |
| `FlattenProperties` | `bool` | `true` | Flatten properties to root level |
| `AsyncFlushIntervalMs` | `int` | `2000` | Timer-based flush interval |
| `MaxAsyncQueueSize` | `int` | `1000` | Trigger immediate flush threshold |
| `RollStrategy` | `string` | `"timestamp"` | `"timestamp"` or `"sequential"` |
| `RollByHour` | `bool` | `false` | Roll files every hour |

---

## 5. Memory -- `PragmaticMemoryProvider`

Unit tests and diagnostic tools need to inspect log entries programmatically without file I/O. The Memory provider stores entries in a thread-safe `ConcurrentQueue` with auto-truncation, predicate-based search, and memory usage estimation.

### Key capabilities

- Thread-safe circular buffer with configurable capacity.
- Search by log level, category, message text, or custom predicate.
- Auto-truncation when capacity is exceeded (removes oldest half).
- Memory usage estimation and entry age tracking.
- `Clear()` for test isolation between test methods.

### Configuration

```csharp
logging.AddProvider<PragmaticMemoryProvider>(_ =>
    new PragmaticMemoryProvider("Memory", PragmaticMemoryConfiguration.ForTesting()));
```

### Memory-specific custom properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxEntries` | `int` | `10000` | Maximum stored entries (max 1M) |
| `AutoTruncate` | `bool` | `true` | Auto-remove oldest when full |

### Querying stored entries

```csharp
var memoryProvider = serviceProvider.GetRequiredService<PragmaticMemoryProvider>();

// All entries at a specific level
var errors = memoryProvider.GetLogEntries(LogLevel.Error);

// Search by message text
var paymentLogs = memoryProvider.GetLogEntriesContaining("payment");

// Custom predicate
var recentWarnings = memoryProvider.GetLogEntries(e =>
    e.LogLevel == LogLevel.Warning && e.Timestamp > DateTime.UtcNow.AddMinutes(-5));

// Most recent N entries
var latest = memoryProvider.GetLatestLogEntries(50);

// Assert in tests
memoryProvider.HasLogEntryContaining("Order created").Should().BeTrue();
```

### Presets

| Preset | Min Level | MaxEntries | Context | Use case |
|--------|-----------|-----------|---------|----------|
| `ForMemory()` | Trace | 10,000 | All | General debugging |
| `ForHighCapacityMemory()` | Trace | 100,000 | All | Long-running diagnostics |
| `ForTesting()` | Information | 1,000 | Off | Unit tests |

---

## 6. Debug -- `PragmaticDebugProvider`

When running under a debugger, developers need log output in the Output window without file or console overhead. The Debug provider writes to `System.Diagnostics.Debug.WriteLine`, automatically adapting its health status based on whether a debugger is attached.

### Key capabilities

- Output to the Visual Studio / Rider Debug Output window.
- Category filtering to focus on specific components.
- Thread ID and timestamp injection.
- Health status: `Healthy` when debugger is attached, `Warning` in release builds.

### Configuration

```csharp
logging.AddProvider<PragmaticDebugProvider>(_ =>
    new PragmaticDebugProvider("Debug", PragmaticDebugConfiguration.ForDebug()));

// Focused debugging on a single service
logging.AddProvider<PragmaticDebugProvider>(_ =>
    new PragmaticDebugProvider("Debug", PragmaticDebugConfiguration.ForFocusedDebug("OrderService")));
```

### Debug-specific custom properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeTimestamp` | `bool` | `true` | Show timestamp prefix |
| `IncludeThreadInfo` | `bool` | `true` | Show `[T01]` thread ID |
| `IncludeCategory` | `bool` | `true` | Show logger category |
| `CategoryFilter` | `string` | `""` | Show only matching categories |

### Presets

| Preset | Min Level | Thread Info | Use case |
|--------|-----------|------------|----------|
| `ForDebug()` | Trace | Yes | General debugging |
| `ForFocusedDebug(filter)` | Debug | Yes | Single component |
| `ForLightweightDebug()` | Information | No | Minimal output |
| `ForPerformanceDebug()` | Trace | Yes | Microsecond timestamps |

---

## 7. Null -- `PragmaticNullProvider`

Benchmarks need to measure the overhead of the logging pipeline itself, without any I/O. The Null provider discards every message with a single `Interlocked.Increment`, providing the absolute baseline for performance measurement.

### Configuration

```csharp
logging.AddProvider<PragmaticNullProvider>(_ =>
    new PragmaticNullProvider("Null", PragmaticNullConfiguration.ForBenchmarking()));
```

### Presets

| Preset | Purpose |
|--------|---------|
| `ForBenchmarking()` | Pure pipeline overhead measurement |
| `ForStructuredBenchmarking()` | Measure structured property serialization cost |
| `ForContextBenchmarking()` | Measure context enrichment cost |
| `ForBatchingBenchmarking()` | Measure batching overhead |
| `ForProductionBenchmarking()` | Simulate production configuration |

---

## 8. Windows Event Log -- `PragmaticWindowsEventLogProvider`

Windows services and enterprise applications often must write to the Windows Event Log for centralized monitoring via SCOM, Splunk, or Windows Event Forwarding. This provider maps log levels to `EventLogEntryType`, supports auto-registration of the Event Source, and truncates messages to the Event Log limit of 31,839 characters.

### Key capabilities

- Automatic `EventLogEntryType` mapping (Error, Warning, Information).
- Auto-registration of Event Source (requires admin privileges).
- Custom Event ID mapping per log level.
- Structured data serialization into the Event Log message body.
- Graceful degradation on non-Windows platforms.
- `[SupportedOSPlatform("windows")]` annotation for trimming safety.

### Configuration

```csharp
logging.AddWindowsEventLog("MyApplication");

// Custom configuration
logging.AddWindowsEventLog(new PragmaticWindowsEventLogConfiguration
{
    SourceName = "OrderService",
    LogName = "Application",
    IncludeStructuredData = true,
    AutoRegisterSource = true,
    MaxMessageLength = 30000
});
```

### Windows Event Log presets

| Preset | Log Name | Structured Data | Use case |
|--------|----------|----------------|----------|
| `Application(name)` | Application | Yes | Standard app logging |
| `Security(name)` | Security | Yes | Security audit |
| `WindowsService(name)` | Application | Yes | Windows services with custom Event IDs |
| `HighVolume(name)` | Application | No | High-throughput (smaller messages) |
| `Development(name)` | Application | Yes (indented) | Debug-friendly output |

---

## Provider Summary

| Provider | Class | Output Target | Async | Batching | Best For |
|----------|-------|--------------|-------|----------|----------|
| Console | `PragmaticConsoleProvider` | stdout | No | No | Development, debugging |
| File | `PragmaticFileProvider` | Disk files | Yes | Yes | Production log files |
| JSON | `PragmaticJsonProvider` | Console / File | No | Yes | Log aggregation (ELK, Fluentd) |
| Enhanced JSON | `PragmaticEnhancedJsonProvider` | File / Console | Yes | Yes | High-throughput NDJSON streaming |
| Memory | `PragmaticMemoryProvider` | In-memory | No | No | Testing, diagnostics |
| Debug | `PragmaticDebugProvider` | Debugger output | No | No | IDE debugging |
| Null | `PragmaticNullProvider` | Nowhere | No | No | Benchmarking |
| Windows Event Log | `PragmaticWindowsEventLogProvider` | Event Log | No | No | Windows services, enterprise |

---

## Creating a Custom Provider

When the built-in providers do not cover your output target, you can implement `IPragmaticLoggerProvider` or, more conveniently, extend `PragmaticLoggerProviderBase`. The base class handles configuration management, metrics, health checks, context enrichment, and privacy redaction -- you only implement `WriteLogCore`.

```csharp
public sealed class SlackAlertProvider : PragmaticLoggerProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;

    public SlackAlertProvider(string webhookUrl, IPragmaticProviderConfiguration configuration)
        : base("Slack", configuration)
    {
        _webhookUrl = webhookUrl;
        _httpClient = new HttpClient();
    }

    protected override void WriteLogCore(LogEntry logEntry)
    {
        // Only send alerts for Error and Critical
        if (logEntry.LogLevel < LogLevel.Error)
            return;

        var payload = new { text = $"[{logEntry.LogLevel}] {logEntry.Category}: {logEntry.Message}" };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Fire-and-forget for alerts (errors tracked by base class metrics)
        _ = _httpClient.PostAsync(_webhookUrl, content);
    }

    protected override void DisposeCore()
    {
        _httpClient.Dispose();
    }
}
```

Register the custom provider through the builder:

```csharp
logging.AddProvider<SlackAlertProvider>(_ =>
    new SlackAlertProvider("https://hooks.slack.com/services/...",
        new PragmaticProviderConfiguration { MinimumLevel = LogLevel.Error }));
```

The `IPragmaticLoggerProvider` interface also exposes `GetMetrics()` and `CheckHealth()` so your custom provider participates in the same observability infrastructure as the built-in providers.
