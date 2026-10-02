---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Logging. Each section shows the wrong approach, the correct approach, and explains wh"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Logging/docs/common-mistakes.md
sidebar:
  order: 7
---
These are the most common issues developers encounter when using Pragmatic.Logging. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Using String Interpolation Instead of Structured Logging

**Wrong:**

```csharp
logger.LogInformation($"Order {orderId} placed by {customerId} for {total:C}");
```

**Right:**

```csharp
logger.LogInformation("Order {OrderId} placed by {CustomerId} for {Total}",
    orderId, customerId, total);
```

**Why:** String interpolation creates a new string on every call, even when the log level is disabled. Structured logging preserves parameter names as semantic properties, enabling search and filtering in log aggregators. The `$""` form allocates unconditionally and loses the structured property names.

For hot paths, use .NET's built-in `[LoggerMessage]` source generator (`Microsoft.Extensions.Logging`):

```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} placed by {CustomerId}")]
private partial void LogOrderPlaced(Guid orderId, string customerId);
```

---

## 2. Forgetting to Call ClearProviders Before Adding Pragmatic

**Wrong:**

```csharp
builder.Services.AddPragmaticLogging(pragmatic => pragmatic
    .AddConsole()); // Now you have TWO console providers
```

**Right:**

```csharp
builder.Logging.ClearProviders(); // Remove the default console provider
builder.Services.AddPragmaticLogging(pragmatic => pragmatic.AddConsole());
```

**Why:** ASP.NET Core registers a default `ConsoleLoggerProvider` automatically. If you add `PragmaticConsoleProvider` without clearing first, every log entry is written to the console twice -- once by each provider. The Pragmatic provider includes structured data, colored output, and redaction; the default provider does not. Clear providers first, then add only the ones you need.

---

## 3. Logging Sensitive Data Without Enabling Redaction

**Wrong:**

```csharp
// No redaction configured -- this writes the password to disk
logger.LogError("Login failed for {Username} with password {Password}", username, password);

// Connection string with embedded credentials
logger.LogInformation("Connected to {ConnectionString}", connectionString);
```

**Right:**

```csharp
// Option A: Don't log sensitive data in the first place
logger.LogError("Login failed for {Username}", username);

// Option B: Enable automatic redaction
pragmatic.EnableDataRedaction(redaction =>
{
    redaction.SensitivePropertyNames = ["password", "secret", "apiKey", "connectionString"];
    redaction.PropertyNamePatterns = [@".*password.*", @".*secret.*", @".*token.*"];
});

// Option C: Enable secret detection for automatic pattern matching
pragmatic.Configure(opts =>
{
    opts.Privacy.EnableSecretDetection = true;
    opts.Privacy.SecretDetection.PrecompilePatterns = true;
});
```

**Why:** The best defense is not logging sensitive data at all. When that is not practical (connection strings in error diagnostics, tokens in debug logs), enable data redaction to catch sensitive values before they reach the output. Secret detection catches patterns like JWT tokens, AWS keys, and connection string passwords even when the property name does not hint at sensitivity.

---

## 4. Using the Development Preset in Production

**Wrong:**

```csharp
// In production code
pragmatic.UseDevelopmentPreset()
    .AddFile("logs/app-{Date}.log");
```

**Right:**

```csharp
pragmatic.UseProductionPreset()
    .AddFile("logs/app-{Date}.log");
```

Or use environment-based selection:

```csharp
if (builder.Environment.IsDevelopment())
    pragmatic.UseDevelopmentPreset();
else
    pragmatic.UseProductionPreset();
```

**Why:** The Development preset sets MinimumLevel to `Trace`, disables background processing, and disables rate limiting. In production, this means:
- Every Debug and Trace message is processed, creating massive log volumes
- Synchronous writes block the calling thread on every log call
- No rate limiting allows a noisy component to fill your disk in minutes

The Production preset enables background processing, sets MinimumLevel to `Information`, enables rate limiting, and turns on correlation IDs.

---

## 5. Leaving File Retention at Its Development Defaults

**Wrong:**

```csharp
// Rolls at 10 MB and keeps 31 files: about 300 MB of history, which a busy service fills in hours
pragmatic.AddFile("logs/app-{Date}.log");
```

**Right:**

```csharp
pragmatic.AddFile("logs/app-{Date}.log", opts =>
{
    opts.CustomProperties["MaxFileSize"] = 100L * 1024 * 1024;  // roll at 100 MB
    opts.CustomProperties["MaxRetainedFiles"] = 30;             // keep 30 files, delete the oldest
});
```

**Why:** The defaults bound the disk a log can take, but they are sized for development: ten-megabyte
files, thirty-one of them. Retention counts files, not days, so size the two together for the volume
the service writes and the history you need to keep. `MaxRetainedFiles = 0` keeps every file — the
setting that lets a log fill the disk.

---

## 6. Placing Enrichment Middleware Too Late in the Pipeline

**Wrong:**

```csharp
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Too late -- all controller logs miss the enrichment context
app.UseMiddleware<LoggingEnrichmentMiddleware>();
```

**Right:**

```csharp
var app = builder.Build();

// Early in the pipeline -- all downstream middleware and handlers benefit
app.UseMiddleware<BaggagePropagationMiddleware>();
app.UseMiddleware<LoggingEnrichmentMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

**Why:** The enrichment middleware creates a logging scope that includes CorrelationId, RequestPath, RequestMethod, and UserId. If it runs after your handlers, the log entries produced during request processing will not have these properties. Place enrichment middleware early so that all downstream components -- including authentication, authorization, and your business logic -- produce enriched log entries.

Note: `BaggagePropagationMiddleware` should run before `LoggingEnrichmentMiddleware` so that distributed tracing context is available for enrichment.

---

## 7. Ignoring Queue Overflow Strategy in High-Throughput Scenarios

**Wrong:**

```csharp
pragmatic.Configure(opts =>
{
    opts.Performance.UseBackgroundProcessing = true;
    opts.Performance.MaxQueueSize = 100; // Very small queue
    // OverflowStrategy defaults to DropOldest -- under load this silently drops log entries
});
```

**Right:**

```csharp
pragmatic.Configure(opts =>
{
    opts.Performance.UseBackgroundProcessing = true;
    opts.Performance.MaxQueueSize = 10000;                                 // size for your peak throughput
    opts.Performance.OverflowStrategy = QueueOverflowStrategy.DropOldest;  // choose the behavior deliberately
});
```

**Why:** With background processing, the default `OverflowStrategy` is `DropOldest`: when the queue fills, the oldest entries are discarded so the caller never blocks. That is the right trade-off for most web APIs, but a queue that is too small silently loses log entries under sustained load — size `MaxQueueSize` for your peak throughput. If you instead choose `Block`, request threads wait for queue space when it is full, which can cause timeouts and cascading failures under load — use it only in compliance-critical scenarios where every log entry must be persisted (audit logs, financial transactions).

---

## 8. Mixing Global and Per-Provider Privacy Configuration

**Wrong:**

```csharp
// Global redaction disabled, but expecting per-provider redaction to work
pragmatic.Configure(opts => opts.Privacy.EnableRedaction = false);

// This provider-level setting has no effect when global is disabled
pragmatic.AddFile("logs/app.log", config =>
{
    config.Privacy.EnableRedaction = true;
    config.Privacy.RedactionMode = RedactionMode.Aggressive;
});
```

**Right:**

```csharp
// Enable global redaction, then configure per-provider
pragmatic.Configure(opts => opts.Privacy.EnableRedaction = true);

// Aggressive redaction for file logs
pragmatic.AddFile("logs/app.log", config =>
{
    config.Privacy.RedactionMode = RedactionMode.Aggressive;
});

// No redaction for in-memory testing — the memory provider uses its own configuration
pragmatic.AddProvider(_ => new PragmaticMemoryProvider("Memory", PragmaticMemoryConfiguration.ForMemory()));
```

**Why:** Privacy redaction runs in the `PragmaticLoggerProviderBase` pipeline. The global `EnableRedaction` flag acts as a master switch. When it is `false`, per-provider redaction settings are ignored because the redaction step is skipped entirely. Enable redaction globally, then use per-provider `RedactionMode` to control aggressiveness per output target.

---

## 9. Not Using Presets When a Standard Configuration Exists

**Wrong:**

```csharp
// Manually configuring 15 settings that match the Production preset
pragmatic.Configure(opts =>
{
    opts.MinimumLevel = LogLevel.Information;
    opts.Performance.UseBackgroundProcessing = true;
    opts.Performance.BufferSize = 5000;
    opts.Context.IncludeCorrelationId = true;
    opts.Context.IncludeUserContext = true;
    opts.RateLimiting.Enabled = true;
    opts.RateLimiting.MaxMessagesPerSecond = 1000;
    opts.EnableTelemetry = true;
    // ... more settings
});
```

**Right:**

```csharp
// Use the preset, then override only what differs
pragmatic
    .UseProductionPreset()
    .Configure(opts => opts.MinimumLevel = LogLevel.Debug); // Override one setting
```

**Why:** Presets exist to provide tested, coherent configurations. Manually replicating a preset means you might miss a setting (like rate limiting burst size) or introduce an inconsistency (like enabling background processing but setting the buffer too small). Start with a preset and override only what your application needs to change.

---

## 10. Exposing Audit Exports Without Authorization

**Wrong:**

```csharp
// Audit export includes sensitive metadata, but the endpoint has no redaction
app.MapGet("/api/audit/export", async (PragmaticAuditService audit) =>
{
    var csv = await audit.ExportAuditAsync(
        from: DateTime.UtcNow.AddDays(-90),
        until: DateTime.UtcNow,
        format: AuditExportFormat.Csv);
    return Results.Text(csv, "text/csv");
});
```

**Right:**

```csharp
// Protect audit export endpoints with authorization
app.MapGet("/api/audit/export", async (PragmaticAuditService audit) =>
{
    var csv = await audit.ExportAuditAsync(
        from: DateTime.UtcNow.AddDays(-90),
        until: DateTime.UtcNow,
        format: AuditExportFormat.Csv);
    return Results.Text(csv, "text/csv");
}).RequireAuthorization("AuditAccess");
```

**Why:** Audit trail data contains metadata about what was redacted, when, and by whom. If you expose audit export endpoints without authorization, you are effectively leaking information about where sensitive data exists in your system. Always protect audit endpoints with appropriate authorization policies.
