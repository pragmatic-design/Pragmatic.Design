# Troubleshooting

Practical problem/solution guide for Pragmatic.Logging. Each section covers a common issue, the likely causes, and the fix.

---

## Log Output Missing Entirely

Your application runs, but no log output appears in any provider.

### Checklist

1. **Did you call `AddPragmaticLogging()`?** The builder must be created before any providers are added.

   ```csharp
   builder.Services.AddPragmaticLogging(pragmatic => pragmatic.AddConsole()); // Required
   ```

2. **Is `Enabled` set to `false`?** Check `appsettings.json` for `"PragmaticLogging": { "Enabled": false }`.

3. **Is the MinimumLevel too high?** If MinimumLevel is `Warning`, all `Information` and `Debug` calls are silently dropped. Check both the preset and any overrides.

4. **Did you add at least one provider?** Calling `AddPragmaticLogging()` alone registers the pipeline but no output. You need at least one provider (`.AddConsole()`, `.AddFile(...)`, etc.).

5. **Are category-level overrides filtering your logger?** Check `appsettings.json` for `"LogLevel"` overrides in the `"Logging"` section that might set your namespace to `None`.

---

## Duplicate Log Entries

Every log message appears twice in the console.

### Cause

You have both the default .NET console provider and `PragmaticConsoleProvider` registered:
`AddPragmaticLogging` keeps the providers already registered, and the host registers the .NET console
one by default.

### Fix

Clear the default providers before adding Pragmatic:

```csharp
builder.Logging.ClearProviders();
builder.Services.AddPragmaticLogging(pragmatic => pragmatic.AddConsole());
```

---

## Context Properties Not Appearing in Log Output

You enabled enrichment but log entries do not contain CorrelationId, UserId, or other context properties.

### Checklist

1. **Is the enrichment middleware registered?** Add it early in the pipeline:

   ```csharp
   app.UseMiddleware<LoggingEnrichmentMiddleware>();
   ```

2. **Is the middleware ordered correctly?** It must run before your handlers. Place it before `UseAuthentication()` and `MapControllers()`.

3. **Is context enrichment enabled in options?**

   ```csharp
   pragmatic.Configure(opts =>
   {
       opts.Context.IncludeCorrelationId = true;
       opts.Context.IncludeUserContext = true;
       opts.Context.IncludeMachineContext = true;
   });
   ```

4. **Is the provider filtering context?** Check if a `ContextFilterConfiguration` with `Mode = ContextFilterMode.None` is set on the specific provider.

5. **For UserId: is the user authenticated?** The `HttpContextProvider` extracts UserId from the `NameIdentifier` claim. If the request is unauthenticated, `UserId` will not appear.

---

## Redaction Not Working

Sensitive data appears in log output despite enabling redaction.

### Checklist

1. **Is global redaction enabled?** The global flag is the master switch:

   ```csharp
   pragmatic.Configure(opts => opts.Privacy.EnableRedaction = true);
   ```

2. **Is the property name in the sensitive list?** Check that your property name matches (case-insensitive) or matches a regex pattern:

   ```csharp
   pragmatic.EnableDataRedaction(r =>
   {
       r.SensitivePropertyNames = ["password", "apiKey", "email"];
       r.PropertyNamePatterns = [@".*password.*", @".*secret.*"];
   });
   ```

3. **For secret detection: is it enabled separately?** Secret detection and data redaction are independent features:

   ```csharp
   pragmatic.Configure(opts =>
   {
       opts.Privacy.EnableRedaction = true;        // Data redaction
       opts.Privacy.EnableSecretDetection = true;  // Secret detection
   });
   ```

4. **Is the confidence threshold too high?** The default is 0.7. If your secrets are not being detected, lower the threshold or use `CreateForDevelopment()` preset (0.5).

5. **Are you using a compliance preset?** Compliance presets automatically configure both redaction and detection:

   ```csharp
   pragmatic.UseCompliancePreset(ComplianceStandard.Gdpr);
   ```

---

## File Provider Not Writing

The file provider is configured but no log files appear on disk.

### Checklist

1. **Does the directory exist?** The file provider creates the file but not the parent directory. Ensure the `logs/` directory (or your configured path) exists.

2. **Does the process have write permissions?** Check file system permissions for the application's service account.

3. **Is the file path template valid?** Placeholders are case-sensitive: `{Date}`, `{Year}`, `{Month}`, `{Day}`, `{Hour}`, `{DateTime}`.

4. **Is background processing blocking?** If `UseBackgroundProcessing` is enabled and the application shuts down before the queue is flushed, entries may be lost. Check that `IHostApplicationLifetime` is properly wired for graceful shutdown.

5. **Is the file locked by another process?** Another instance of the application or a log viewer may have the file locked. Check with your file system.

---

## Rate Limiting Dropping Important Messages

Critical error messages are being dropped by the rate limiter.

### Cause

Rate limiting applies to all log levels equally by default.

### Fix

Configure rate limiting with appropriate thresholds, or apply it only to specific levels:

```csharp
pragmatic.EnableRateLimiting(rl =>
{
    rl.MaxMessagesPerSecond = 1000;
    rl.BurstSize = 100;
    rl.Strategy = "TokenBucket";
});

// Or use built-in presets that only limit lower-priority levels
// Error and Critical are typically exempt from rate limiting
```

For compliance scenarios, disable rate limiting entirely and rely on the bounded queue with `Block` overflow strategy instead.

---

## High Memory Usage from Logging

The application's memory footprint grows over time due to logging.

### Checklist

1. **Is the Memory provider accumulating entries?** `PragmaticMemoryProvider` stores entries in-memory. Check `MaxEntries` and ensure `AutoTruncate` is enabled.

2. **Is the background queue growing unbounded?** If using `QueueOverflowStrategy.Expand`, the queue grows without limit. Switch to `DropOldest` or set a reasonable `MaxQueueSize`.

3. **Is zero-allocation mode enabled?** Enable it to reduce GC pressure:

   ```csharp
   pragmatic.Configure(opts => opts.Performance.EnableZeroAllocation = true);
   ```

---

## Slow Application Startup with PrecompilePatterns

Application startup takes several seconds when secret detection is enabled.

### Cause

`PrecompilePatterns = true` compiles all regex patterns at startup. With the full pattern library, this can take 1-2 seconds.

### Fix

For development, disable precompilation:

```csharp
pragmatic.Configure(opts =>
{
    opts.Privacy.SecretDetection.PrecompilePatterns = false;
});
```

For production, accept the startup cost (patterns are compiled once) or limit the number of patterns:

```csharp
pragmatic.Configure(opts =>
{
    opts.Privacy.SecretDetection.PrecompilePatterns = true;
    opts.Privacy.SecretDetection.MaxPatternsPerScan = 20; // Fewer patterns, faster startup
});
```

---

## FAQ

### Can I use Pragmatic.Logging with Serilog or NLog?

Yes, but there is no benefit. Pragmatic.Logging replaces these libraries entirely. It registers as an `ILoggerProvider` in the standard .NET logging infrastructure. If you add both Pragmatic and Serilog providers, log entries will be processed by both pipelines independently.

### Does Pragmatic.Logging support OpenTelemetry?

The `CorrelationIdProvider` integrates with `Activity.Current` and propagates W3C `traceparent` headers. The `BaggagePropagationMiddleware` forwards context values to downstream services. OpenTelemetry exporters can consume these traces.

### Can I use different redaction settings per provider?

Yes. Each provider has its own `PrivacyConfiguration`. Enable global redaction, then configure per-provider `RedactionMode`:

```csharp
// Aggressive redaction for file logs
pragmatic.AddFile("logs/app.log", c => c.Privacy.RedactionMode = RedactionMode.Aggressive);

// No redaction for memory provider (testing) — it uses its own configuration
pragmatic.AddProvider(_ => new PragmaticMemoryProvider("Memory", PragmaticMemoryConfiguration.ForMemory()));
```

### How do I test that redaction is working?

Use the `PragmaticMemoryProvider` with redaction enabled and assert on the stored entries:

```csharp
var memoryProvider = serviceProvider.GetRequiredService<PragmaticMemoryProvider>();
var entries = memoryProvider.GetLogEntriesContaining("[REDACTED]");
entries.Should().NotBeEmpty();
```

### What is the BootstrapLogger for?

The `BootstrapLogger` provides logging before the DI container is built. Use it for startup diagnostics:

```csharp
using var bootstrap = BootstrapLogger.Create();
bootstrap.LogInformation("Loading configuration...");
```

It writes to the console and is disposed when the full logging pipeline takes over.

---

## Getting Help

1. Check the [concepts guide](concepts.md) for architecture overview
2. Check the [getting started guide](getting-started.md) for setup instructions
3. Review [Benchmark Results](../BENCHMARK-RESULTS.md) for performance comparisons
4. Open an issue on the Pragmatic.Design repository with:
   - Your `PragmaticLoggingBuilder` configuration code
   - The `PragmaticLogging` section from `appsettings.json`
   - The expected vs actual behavior
   - .NET version and OS
