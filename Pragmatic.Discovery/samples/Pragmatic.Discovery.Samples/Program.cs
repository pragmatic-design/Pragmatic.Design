// Pragmatic.Discovery Samples - entry point. Runs every feature sample end to end.

using Pragmatic.Discovery.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Discovery Samples                      ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Topology parsing (sync).
TopologyParsingSample.Run();

// Backends.
await InMemoryBackendSample.RunAsync();
await CustomBackendSample.RunAsync();

// Service + DI.
await DiscoveryServiceSample.RunAsync();

// Validation.
await ValidationSample.RunAsync();

// Startup lifecycle & options.
await HostedServiceSample.RunAsync();
await StartupOptionsSample.RunAsync();
OptionsConfigurationSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
