---
title: "Performance and Zero-Allocation"
description: "Logging sits on the hot path of every request. A single `logger.LogInformation(...)` call that allocates a few strings and a dictionary can, under high throughp"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Logging/docs/performance.md
sidebar:
  order: 4
---
Logging sits on the hot path of every request. A single `logger.LogInformation(...)` call that allocates a few strings and a dictionary can, under high throughput, generate enough GC pressure to cause visible latency spikes. Pragmatic.Logging provides zero-allocation formatting, object pooling, queue-based background processing, rate limiting, and batching -- all configurable through presets that match your deployment scenario.

---

## 1. Zero-Allocation Logging

The standard `string.Format` or interpolated string approach allocates a new string on every log call, even when the log level is disabled. Pragmatic.Logging eliminates these allocations through span-based formatting, stack-allocated buffers, and ArrayPool rentals.

### StackAllocatedBuffer

Small messages (up to 1,024 characters) are formatted into a `stackalloc` buffer, avoiding heap allocation entirely. Larger messages fall back to `ArrayPool<char>.Shared` so the rented array is reused across calls.

```csharp
// The runtime chooses stack or pool based on length
StackAllocatedBuffer.WithBuffer(256, (Span<char> buffer) =>
{
    // Format directly into the span -- no heap allocation
    var written = FormatOrderMessage(buffer, orderId, total);
    WriteToOutput(buffer[..written]);
});
```

The threshold is controlled by the constant `StackAllocatedBuffer.StackAllocThreshold` (1,024 chars). A generic overload accepts an `ISpanCallback<TResult, TState>` for operations that need to return a value from the span operation.

### ZeroAllocMessageFormatter

The `ZeroAllocMessageFormatter` is the core formatting engine. It uses a `ThreadLocal<StringBuilder>` pool and `ArrayPool<object?>` for parameter arrays, minimizing allocations on every call.

```csharp
// Fast-path for 1-3 parameters (most common case)
var message = ZeroAllocMessageFormatter.FormatFast(
    "Order {OrderId} placed by {CustomerId}".AsSpan(),
    orderId,
    customerId);

// Span-based formatting with zero heap allocation for small outputs
Span<char> buffer = stackalloc char[256];
var success = ZeroAllocMessageFormatter.TryFormat(
    "User {UserId} logged in at {Timestamp}".AsSpan(),
    new object[] { "john.doe", DateTime.UtcNow },
    buffer,
    out var written);
```

For hot paths where even the parameter array allocation matters, rent from the pool:

```csharp
var paramArray = ZeroAllocMessageFormatter.RentParameterArray(2);
try
{
    paramArray[0] = orderId;
    paramArray[1] = amount;
    var message = ZeroAllocMessageFormatter.Format(
        "Processing order {OrderId} for {Amount}".AsSpan(), paramArray);
}
finally
{
    ZeroAllocMessageFormatter.ReturnParameterArray(paramArray);
}
```

### Optimized Type Formatting

The formatter has specialized handlers for common types that call `TryFormat` directly on the `Span<char>` destination, bypassing `ToString()` entirely.

| Type | Allocation | Method |
|------|-----------|--------|
| `int`, `long`, `float`, `double`, `decimal` | Zero | `TryFormat(Span<char>)` |
| `DateTime`, `DateTimeOffset` | Zero | `TryFormat` with ISO 8601 |
| `TimeSpan`, `Guid` | Zero | `TryFormat` |
| `bool` | Zero | Literal `"true"` / `"false"` copy |
| `string` | Zero | `AsSpan().CopyTo()` |
| `ISpanFormattable` | Zero | Interface `TryFormat` |
| Other | 1 allocation | `ToString()` fallback |

### Early-Exit IsEnabled Checks

Every provider performs an `IsEnabled` check with `[MethodImpl(AggressiveInlining)]` before any formatting work begins. If the log level is below the configured minimum (or filtered by category), the call returns immediately with zero work done.

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public bool IsEnabled(string categoryName, LogLevel logLevel)
{
    if (logLevel == LogLevel.None)
        return false;

    // Category-specific level check (fast dictionary lookup)
    if (_configuration.CategoryLevels.TryGetValue(categoryName, out var categoryLevel))
        return logLevel >= categoryLevel;

    // Wildcard pattern fallback
    // ...

    return logLevel >= _configuration.MinimumLevel;
}
```

### High-performance logging methods

For low-overhead logging on hot paths, use .NET's built-in `[LoggerMessage]` source generator
(`Microsoft.Extensions.Logging`). It emits an `IsEnabled`-guarded partial method backed by a cached
delegate, avoiding string interpolation and boxing:

```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Processed {Count} items in {ElapsedMs}ms")]
private partial void LogProcessed(int count, long elapsedMs);
```

The provider-side hot path (formatting, buffering) uses the zero-allocation infrastructure below
(`ZeroAllocMessageFormatter`, pooled buffers), independent of how the call site is written.

---

## 2. Performance Presets

Pragmatic.Logging ships with four performance presets that configure all the knobs at once. Each preset is available through `PragmaticLoggingOptions.ConfigurationPreset`.

| Preset | Min Level | Zero-Alloc | Batching | Background | Rate Limiting | Privacy | Use Case |
|--------|-----------|-----------|----------|------------|--------------|---------|----------|
| **Development** | Debug | Off | Off | Off | Off | Minimal | Local debugging |
| **Production** | Information | On | On (100/5s) | On | Optional | Standard | Typical web API |
| **HighPerformance** | Information | On | On (200/10s) | On | On | Off | High-throughput services |
| **Compliance** | Information | On | On (50/2s) | On | Off | Aggressive | Regulated industries |

### Applying a Preset

```csharp
// Via appsettings.json
{
  "PragmaticLogging": {
    "ConfigurationPreset": "Production"
  }
}

// Via code
services.AddPragmaticLogging(logging =>
{
    logging.AddConsole(config =>
    {
        config.Performance.EnableBatching = true;
        config.Performance.BatchSize = 100;
        config.Performance.FlushInterval = TimeSpan.FromSeconds(5);
        config.Performance.UseZeroAllocation = true;
        config.Performance.MaxQueueSize = 10000;
        config.Performance.OverflowStrategy = QueueOverflowStrategy.DropOldest;
    });
});
```

---

## 3. Rate Limiting

Under sustained load, a noisy log category can produce thousands of duplicate messages per second, overwhelming both the logging pipeline and downstream aggregators. The `HighPerformanceRateLimiter` throttles log output using one of three strategies without blocking the caller.

### Strategies

| Strategy | Algorithm | Behavior |
|----------|-----------|----------|
| `TokenBucket` | Token bucket | Allows bursts up to bucket size, then rate-limits. Best for most logging scenarios. |
| `SlidingWindow` | Sliding window | Precise rate over a moving window. Best for strict compliance requirements. |
| `FixedWindow` | Fixed window | Simple counter reset at window boundary. Fastest, but allows edge bursts. |

### Configuration

```csharp
// Apply rate limiting via filter expressions
services.AddPragmaticLogging(null, globalConfig =>
{
    // Limit error logs to 10 per minute (token bucket)
    globalConfig.AddFilter(ctx => ctx.RateLimitTokenBucket(10, TimeSpan.FromMinutes(1)));
}, logging =>
{
    logging.AddConsole();
});
```

### Built-in Presets

The `LoggingRateLimitPresets` class provides tuned configurations for common scenarios.

| Preset | Strategy | Max Messages | Window | Description |
|--------|----------|-------------|--------|-------------|
| `ErrorLogs` | TokenBucket | 10 | 1 min | Prevents error spam |
| `WarningLogs` | TokenBucket | 50 | 1 min | Moderate throttle |
| `DebugLogs` | SlidingWindow | 100 | 10 sec | Strict debug limiting |
| `HealthCheckLogs` | FixedWindow | 1 | 5 min | One per interval |

### RateLimitingOptions (appsettings.json)

```json
{
  "PragmaticLogging": {
    "RateLimiting": {
      "Enabled": true,
      "MaxMessagesPerSecond": 1000,
      "BurstSize": 100,
      "Strategy": "TokenBucket"
    }
  }
}
```

---

## 4. Background Processing

Synchronous writes to disk or network block the calling thread. Pragmatic.Logging uses queue-based async processing to decouple log production from log output.

### How It Works

When `PerformanceConfiguration.EnableBatching` is `true`, log entries are enqueued into a bounded channel instead of being written synchronously. A background consumer thread (running at `BelowNormal` priority by default) drains the queue and writes entries to the provider in batches.

```
Caller thread              Background thread
     |                           |
     |-- Enqueue(entry) -->      |
     |   (non-blocking)          |
     |                      [Wait for batch/timer]
     |                           |
     |                      [WriteLogCore(batch)]
     |                           |
     |                      [Flush to disk/network]
```

### Queue Overflow Strategies

When the queue fills up faster than the background thread can drain it, the `QueueOverflowStrategy` determines what happens.

| Strategy | Behavior | Data Loss | Blocking |
|----------|----------|-----------|----------|
| `DropOldest` | Remove oldest entries to make room | Yes (oldest) | No |
| `DropNewest` | Reject new entries when full | Yes (newest) | No |
| `Block` | Block the caller until space is available | No | Yes |
| `Expand` | Dynamically grow the queue | No | No |

For production, `DropOldest` is recommended -- it prevents caller blocking while preserving the most recent (and usually most relevant) entries. Use `Block` only in compliance-critical scenarios where every entry must be persisted.

### Configuration

```csharp
config.Performance = new PerformanceConfiguration
{
    EnableBatching = true,
    BatchSize = 100,                               // Entries per write batch
    FlushInterval = TimeSpan.FromSeconds(5),        // Timer-based flush
    MaxQueueSize = 10000,                           // Bounded queue capacity
    OverflowStrategy = QueueOverflowStrategy.DropOldest,
    UseZeroAllocation = true,
    BackgroundThreadPriority = ThreadPriority.BelowNormal
};
```

---

## 5. Batching

Individual writes to disk are expensive due to system call overhead. Batching amortizes this cost by collecting multiple log entries and writing them in a single I/O operation.

### How Batching Works

The `SimpleBatchingProvider` and `BatchingProvider` base classes accumulate entries until either the `BatchSize` threshold or the `FlushTimeout` is reached, whichever comes first. The batch is then written atomically (or as a single buffered write, depending on the provider).

### BatchingOptions

```csharp
var batchingOptions = new BatchingOptions
{
    BatchSize = 100,                              // Flush every 100 entries
    FlushTimeout = TimeSpan.FromSeconds(1),       // Or every 1 second
    UseBackgroundProcessing = true,               // Async drain
    MaxQueueSize = 10000                          // Bounded capacity
};
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `BatchSize` | `int` | `100` | Maximum entries per batch |
| `FlushTimeout` | `TimeSpan` | `1s` | Maximum wait before flushing |
| `UseBackgroundProcessing` | `bool` | `true` | Use dedicated background thread |
| `MaxQueueSize` | `int` | `10000` | Maximum queued entries |

### Batch Size Tuning

Larger batches reduce I/O overhead but increase memory usage and latency before entries appear in the output. Smaller batches reduce latency but increase system call frequency.

| Scenario | Recommended Batch Size | Flush Interval |
|----------|----------------------|----------------|
| Web API (moderate traffic) | 50-100 | 1-2 seconds |
| High-throughput service | 200-500 | 5-10 seconds |
| Real-time streaming | 10-20 | 100 ms |
| Compliance (every entry matters) | 25-50 | 500 ms |

---

## 6. Configuration Reference

### PerformanceOptions (appsettings.json)

These options are bound from the `PragmaticLogging:Performance` section.

```json
{
  "PragmaticLogging": {
    "Performance": {
      "BufferSize": 1000,
      "FlushThreshold": 100,
      "EnableZeroAllocation": true,
      "UseBackgroundProcessing": true,
      "BackgroundQueueSize": 10000
    }
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `BufferSize` | `int` | `1000` | Internal buffer capacity |
| `FlushThreshold` | `int` | `100` | Entries before auto-flush |
| `EnableZeroAllocation` | `bool` | `true` | Use span-based formatting |
| `UseBackgroundProcessing` | `bool` | `true` | Async queue processing |
| `BackgroundQueueSize` | `int` | `10000` | Background queue capacity |

### PerformanceConfiguration (per-provider)

Each provider has its own `PerformanceConfiguration` instance for fine-grained control.

```csharp
var config = new PerformanceConfiguration
{
    EnableBatching = true,
    BatchSize = 100,
    FlushInterval = TimeSpan.FromSeconds(5),
    MaxQueueSize = 10000,
    OverflowStrategy = QueueOverflowStrategy.DropOldest,
    UseZeroAllocation = true,
    BackgroundThreadPriority = ThreadPriority.BelowNormal
};
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableBatching` | `bool` | `true` | Enable batch processing |
| `BatchSize` | `int` | `100` | Entries per batch |
| `FlushInterval` | `TimeSpan` | `5s` | Timer-based flush |
| `MaxQueueSize` | `int` | `10000` | Bounded queue size |
| `OverflowStrategy` | `QueueOverflowStrategy` | `DropOldest` | Queue full behavior |
| `UseZeroAllocation` | `bool` | `true` | Zero-alloc formatting |
| `BackgroundThreadPriority` | `ThreadPriority` | `BelowNormal` | Background thread priority |

### RateLimitingOptions (appsettings.json)

```json
{
  "PragmaticLogging": {
    "RateLimiting": {
      "Enabled": true,
      "MaxMessagesPerSecond": 1000,
      "BurstSize": 100,
      "Strategy": "TokenBucket"
    }
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Enabled` | `bool` | `false` | Enable rate limiting |
| `MaxMessagesPerSecond` | `int` | `1000` | Steady-state rate |
| `BurstSize` | `int` | `100` | Allowed burst above rate |
| `Strategy` | `string` | `"TokenBucket"` | `TokenBucket`, `FixedWindow`, `SlidingWindow` |

---

## Benchmark Guidance

The harnesses live in `benchmarks/Pragmatic.Logging.Benchmarks/`, and the latest run, with its machine,
command and reports, is in [BENCHMARK-RESULTS.md](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Logging/BENCHMARK-RESULTS.md).

**Measure a sink that does the work.** `PragmaticNullProvider` takes the deferred path and never renders
the message or reads the state, so it measures how cheaply a call can be skipped, not what logging costs.
The library comparison uses a sink per library that renders the message and reads every property, and
checks before timing that all of them consumed the same event (`dotnet run -c Release -- verify`).

The presets on `PragmaticNullConfiguration` (`ForBenchmarking`, `ForStructuredBenchmarking`,
`ForContextBenchmarking`, `ForBatchingBenchmarking`, `ForProductionBenchmarking`) are still the
configurations to compare: pass one to a provider that writes, and the difference between two runs is the
cost of the feature the preset turns on. Declared redaction is measured that way, on its own
(`dotnet run -c Release -- redaction`): in the last run it took a call from 749 ns to 1,454 ns.
