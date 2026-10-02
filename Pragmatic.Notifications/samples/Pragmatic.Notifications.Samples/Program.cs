using Pragmatic.Notifications.Samples.Samples;

Console.WriteLine("=== Pragmatic.Notifications Samples ===\n");

// Every sample uses NotificationTestHarness — an in-memory INotificationService
// that records every send for assertion. Zero external infrastructure required
// (no SMTP server, no webhook sink, no database). In real applications this
// harness is also the recommended stand-in for the INotificationService
// dependency under test.

await BasicSendSample.Run();
await RecipientResolutionSample.Run();
await AudienceAndPrioritySample.Run();
await CategoryAndTrackingSample.Run();
await EnqueuedVsSyncSample.Run();

// Real-pipeline samples: these build a DI container / host and exercise the actual
// routing, resolution, channel, store, and background-delivery code — using in-memory
// fakes for transports so no external infrastructure is required.
await CustomChannelSample.RunAsync();
await MultiChannelConfigurationSample.RunAsync();
await ChannelOverrideSample.RunAsync();
await CustomRecipientResolverSample.RunAsync();
await CustomPreferenceProviderSample.RunAsync();
await SmtpChannelSample.RunAsync();
await WebhookChannelSample.RunAsync();
await EfCoreStoreSample.RunAsync();
await BackgroundDeliverySample.RunAsync();
DiagnosticsMetricsSample.Run();

Console.WriteLine("\n=== All samples completed. ===");
