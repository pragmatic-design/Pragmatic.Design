using Pragmatic.Jobs.Samples.Samples;

// Runnable scenarios built on the in-memory job store (AddPragmaticJobs in
// standalone mode). Self-contained: no Docker, no EF Core, no database.
// Each scenario stands up a minimal Generic Host, schedules work, lets the
// JobProcessorService run long enough to drain it, then shuts down cleanly.
//
// Companion job-definition files (DailyReportJob.cs, SendReminderJob.cs,
// GenerateInvoiceJob.cs, RecalculatePricesJob.cs, CleanupExpiredSessionsJob.cs)
// declare the jobs that the SG picks up. The SG emits the type registry and
// per-job invokers that these scenarios depend on.

Console.WriteLine("=== Pragmatic.Jobs Samples ===\n");

await ScheduleAndAwaitSample.Run();
await ParameterizedJobSample.Run();
await DelayedSchedulingSample.Run();
await RetryOnTransientFailureSample.Run();
await ContinuationChainSample.Run();

// Newer scenarios closing sample gaps from the module review.
await CancellationSample.Run();
await TimeoutEnforcementSample.Run();
await DiagnosticsSample.Run();
await EfCorePersistenceSample.Run();
await DistributedLockingSample.Run();
await MessageSchedulerBridgeSample.Run();

Console.WriteLine("\n=== All samples completed. ===");
