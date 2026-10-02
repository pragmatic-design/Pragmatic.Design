using Pragmatic.Resilience.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Resilience Samples                     ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1-2. Fluent builder + named policies (existing)
await FluentBuilderSample.RunAsync();
await NamedPoliciesSample.RunAsync();

// 3-4. SG attributes + error types (existing)
await SourceGeneratorSample.RunAsync();
ErrorTypesSample.Show();

// 5-6. Runnable demos (new)
await RetryDemoSample.RunAsync();
await TimeoutDemoSample.RunAsync();

// 7. Circuit breaker with state transitions
await CircuitBreakerDemoSample.RunAsync();

// 8-11. Hedging, rate limiter, fallback, and Result bridge
await HedgingDemoSample.RunAsync();
await RateLimiterDemoSample.RunAsync();
await FallbackDemoSample.RunAsync();
await ResultBridgeSample.RunAsync();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
