---
title: "Pragmatic.Logging -- Getting Started"
description: "This guide covers installation, basic setup, privacy configuration, and common usage patterns."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Logging/docs/getting-started.md
sidebar:
  order: 2
---
This guide covers installation, basic setup, privacy configuration, and common usage patterns.

## Installation

```bash
dotnet add package Pragmatic.Logging
```

## Step 1: Register Logging

Call `services.AddPragmaticLogging(...)` and configure the `PragmaticLoggingBuilder` it hands you. It
replaces `ILoggerFactory` with Pragmatic's and keeps the providers already registered:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPragmaticLogging(pragmatic => pragmatic
    .UseProductionPreset()
    .AddConsole()
    .AddFile("logs/app-{Date}.log"));
```

In a Pragmatic host the same builder is reached from `app.UseLogging(pragmatic => …)`. There is one
builder, `Pragmatic.Logging.Extensions.PragmaticLoggingBuilder`; there is no
`ILoggingBuilder.AddPragmaticLogging()`.

## Step 2: Choose a Preset

Presets configure sensible defaults for common scenarios. You can override individual settings after applying a preset.

| Preset | Method | Best For |
|--------|--------|----------|
| Development | `UseDevelopmentPreset()` | Local development. Verbose, no redaction. |
| Production | `UseProductionPreset()` | Production deployments. Rate limiting, correlation IDs. |
| High Performance | `UseHighPerformancePreset()` | High-throughput services. Zero-allocation, batched writes. |
| Compliance | `UseCompliancePreset(standard)` | Regulated environments. Redaction, audit trail. |

```csharp
// Override specific settings after a preset
pragmatic
    .UseProductionPreset()
    .Configure(opts => opts.MinimumLevel = LogLevel.Debug); // Override minimum level
```

## Step 3: Add Providers

Providers determine where log output goes. Add as many as needed:

```csharp
pragmatic
    .AddConsole()                                        // PragmaticConsoleConfiguration.ForAdvancedConsole()
    .AddFile("logs/app-{Date}.log", config => config.MinimumLevel = LogLevel.Information)
    .AddJson("logs/app.json")
    .AddNdjsonAsync("logs/app.ndjson")
    .AddProvider(_ => new PragmaticMemoryProvider("Memory", PragmaticMemoryConfiguration.ForMemory())); // For testing
```

Each provider takes a `PragmaticProviderConfiguration`: directly, or through an action that edits the
provider's defaults.

### File Provider Rolling

The file provider supports template-based naming with automatic rolling:

```
"logs/app-{Date}.log"                     -> app-2026-03-21.log
"logs/{Year}/{Month}/app-{Day}.log"       -> logs/2026/03/app-21.log
"logs/app-{DateTime}.log"                 -> app-2026-03-21-14-30-25.log
"logs/app-{Hour}.log"                     -> app-14.log
```

Rolling intervals: `Hour`, `Day`, `Week`, `Month`, `Year`, `None`.

## Step 4: Use Standard ILogger

After registration, inject and use `ILogger<T>` as usual. Pragmatic.Logging handles enrichment, redaction, and routing to providers behind the scenes.

```csharp
public class OrderService(ILogger<OrderService> logger)
{
    public void ProcessOrder(int orderId)
    {
        logger.LogInformation("Processing order {OrderId}", orderId);
    }
}
```

---

## Privacy Configuration

### Enable Secret Detection

Secret detection scans log content for API keys, tokens, connection strings, and other sensitive data:

```csharp
pragmatic.Configure(opts =>
{
    opts.Privacy.EnableSecretDetection = true;
    opts.Privacy.SecretDetection.PrecompilePatterns = true;
    opts.Privacy.SecretDetection.MinimumConfidenceForRedaction = 0.7;
});
```

### Enable Data Redaction

Data redaction replaces sensitive values in log output:

```csharp
pragmatic.EnableDataRedaction(redaction =>
{
    redaction.RedactionPlaceholder = "[REDACTED]";
    redaction.SensitivePropertyNames = ["password", "apiKey", "connectionString"];
    redaction.PropertyNamePatterns = [@".*secret.*", @".*token.*"];
    redaction.MessageRedactionPatterns = [@"[\w.+-]+@[\w-]+\.[\w.]+"];  // Email
});
```

### Compliance Presets

For regulated environments, use a compliance preset that configures both detection and redaction:

```csharp
// GDPR: complete redaction, high confidence, audit trail
pragmatic.UseCompliancePreset(ComplianceStandard.Gdpr);

// HIPAA: very high confidence threshold
pragmatic.UseCompliancePreset(ComplianceStandard.Hipaa);

// PCI-DSS: maximum confidence threshold
pragmatic.UseCompliancePreset(ComplianceStandard.PciDss);
```

---

## ASP.NET Core Integration

### Enrichment Middleware

Add the enrichment middleware to automatically attach context properties to every log entry:

```csharp
var app = builder.Build();
app.UseMiddleware<LoggingEnrichmentMiddleware>();
```

This adds: CorrelationId, RequestPath, RequestMethod, UserId (from claims), MachineName, ProcessId.

### Bootstrap Logger

For logging before the DI container is built (e.g., during startup configuration):

```csharp
using var bootstrap = BootstrapLogger.Create();
bootstrap.LogInformation("Starting application...");

var builder = WebApplication.CreateBuilder(args);
// ... configure services ...
```

---

## Rate Limiting

Prevent log spam in noisy scenarios:

```csharp
pragmatic.EnableRateLimiting(rl =>
{
    rl.MaxMessagesPerSecond = 1000;
    rl.BurstSize = 100;
    rl.Strategy = "TokenBucket";  // Also: FixedWindow, SlidingWindow
});
```

---

## Audit Trail

Enable audit trail for compliance requirements:

```csharp
pragmatic.EnableAuditTrail(audit =>
{
    audit.StorageType = "FileSystem";   // FileSystem or Memory
    audit.PolicyType = "GDPR";
    audit.BatchSize = 100;
    audit.FlushIntervalSeconds = 30;
});
```

---

## Configuration via appsettings.json

All options bind to the `PragmaticLogging` configuration section:

```json
{
  "PragmaticLogging": {
    "Enabled": true,
    "MinimumLevel": "Information",
    "Privacy": {
      "EnableRedaction": true,
      "EnableSecretDetection": true
    },
    "Providers": {
      "Console": { "Enabled": true, "UseColors": true },
      "File": { "Enabled": true, "BasePath": "./logs" }
    }
  }
}
```

---

## Custom Providers

Implement `IPragmaticLoggerProvider` and register via the builder:

```csharp
pragmatic.AddProvider<MyCustomProvider>(sp =>
{
    var config = PragmaticProviderConfiguration.CreateDefault();
    return new MyCustomProvider("custom", config);
});
```
