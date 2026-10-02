using Pragmatic.Logging.Samples.Samples;

Console.WriteLine("=== Pragmatic.Logging Samples ===\n");

// Each sample builds its own ServiceCollection with a Pragmatic logging pipeline,
// resolves an ILogger<T>, emits representative log lines, then disposes the provider.
// Keep scenarios short and focused: one concept per sample.

BasicLoggingSample.Run();
StructuredLoggingSample.Run();
ScopesSample.Run();
RedactionSample.Run();
MultipleProvidersSample.Run();

// Additional scenarios covering the broader feature surface.
ContextManagerSample.Run();
CorrelationIdSample.Run();
MemoryProviderSample.Run();
FileProvidersSample.Run();
RateLimitingSample.Run();
CompliancePresetSample.Run();
BootstrapLoggerSample.Run();
await AuditTrailSample.RunAsync();

Console.WriteLine("\n=== All samples completed. ===");
