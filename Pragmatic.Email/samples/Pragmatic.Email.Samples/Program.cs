using Pragmatic.Email.Samples.Samples;

Console.WriteLine("=== Pragmatic.Email Samples ===\n");

// Every sample uses InMemoryTransport — a drop-in IEmailTransport that records
// every send for assertion. No SMTP server, no DKIM keys, no network required.
// In real applications the transport is resolved via DI; here we construct it
// directly so the samples stay self-contained.
//
// Note: these samples send through the transport directly, which skips the
// EmailSender middleware pipeline (validation, DKIM signing, retries). When
// you want to exercise the pipeline, wire EmailSender + InMemoryTransport
// via AddEmailTestHarness() in a ServiceCollection.

// Message building + in-memory transport basics.
await BasicSendSample.Run();
await MultipleRecipientsSample.Run();
await HtmlAndTextBodySample.Run();
await AttachmentsSample.Run();
await TestHarnessAssertionsSample.Run();

// DI wiring + host integration: these resolve the public IEmailSender, which runs
// the full middleware pipeline before handing off to the transport.
await DependencyInjectionSample.Run();
await PragmaticHostIntegrationSample.Run();
await DefaultFromSample.Run();
await CustomMiddlewareSample.Run();

// Transports.
await SmtpTransportSample.Run();

// Security middleware (DKIM, S/MIME) exercised through the pipeline.
await DkimSigningSample.Run();
await SmimeSigningSample.Run();

// Observability.
await DiagnosticsSample.Run();

Console.WriteLine("\n=== All samples completed. ===");
