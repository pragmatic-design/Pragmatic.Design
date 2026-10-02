using Pragmatic.Messaging.Samples.Samples;

Console.WriteLine("=== Pragmatic.Messaging Samples ===\n");

// Runnable scenarios built on the in-memory message bus (AddPragmaticMessaging
// in InMemory mode). Self-contained: no Docker, no RabbitMQ, no Kafka, no
// database. Transport-backed and outbox scenarios will land as separate sample
// tiers once the corresponding infrastructure is optional/opt-in.
//
// Companion type-declaration files in this project exist for reference only
// (BatchSample.cs, OrderSagaSample.cs). They illustrate the Batch and Saga
// shapes through code; they are not executed by Program.cs.

await InMemoryRoundTripSample.Run();
await MultipleMessageTypesSample.Run();
await CorrelationContextSample.Run();
await SendVsPublishSample.Run();
await SagaFlowSample.Run();

Console.WriteLine("\n=== All samples completed. ===");
