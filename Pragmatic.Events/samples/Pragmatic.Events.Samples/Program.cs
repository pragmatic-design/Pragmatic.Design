using Pragmatic.Events.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Events Samples                         ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1. Basic: entity raises events, dispatcher invokes handlers
await BasicDispatchSample.RunAsync();

// 2. Handler ordering: deterministic execution via Order property
await HandlerOrderingSample.RunAsync();

// 3. Error resilience: dispatch continues when a handler throws
await ErrorResilienceSample.RunAsync();

// 4. Multi-event: accumulation and batch dispatch
await MultiEventSample.RunAsync();

// ── Advanced ─────────────────────────────────────────────────────────────────

// 5. Registration strategies: manual, assembly scanning, SG-generated
await RegistrationSample.RunAsync();

// 6. TimeProvider: testable timestamps, fake clock for tests
await TimeProviderSample.RunAsync();

// 7. EF Core integration: the lifecycle interceptor raises, the unit of work dispatches
EfCorePatternSample.Run();

// 8. Transactional outbox: capture in-transaction, deliver asynchronously (runnable on SQLite)
await OutboxSample.RunAsync();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
