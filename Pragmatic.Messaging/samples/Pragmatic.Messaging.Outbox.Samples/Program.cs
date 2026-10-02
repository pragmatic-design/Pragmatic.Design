using Pragmatic.Messaging.Outbox.Samples;

Console.WriteLine("=== Pragmatic.Messaging samples ===\n");

// Self-contained, runnable scenarios over the in-memory message bus plus the
// opt-in feature stores (dead-letter, idempotency, scheduling, auditing,
// middleware, testing harness, SG resilience pipeline). The transactional
// outbox runs against SQLite in-memory. Broker/EF-only features (Kafka, EFCore
// stores) ship as compiling setup-only samples with explanatory comments —
// they require external infrastructure to execute.

await MiddlewareSample.RunAsync();
await DeadLetterSample.RunAsync();
await IdempotencySample.RunAsync();
await ScheduledMessagesSample.RunAsync();
await AuditingSample.RunAsync();
await TestingHarnessSample.RunAsync();
await ResilienceSample.RunAsync();
await ReliabilityPipelineSample.RunAsync();
await DistributedRpcSample.RunAsync();
await MultiBusSample.RunAsync();
await ChoreographySample.RunAsync();
await ClaimCheckSample.RunAsync();
await SqlTransportSample.RunAsync();
await EfCoreStoresSample.RunAsync();
KafkaTransportSample.Describe();

await OutboxSample.RunAsync();

Console.WriteLine("\n=== All samples completed. ===");
