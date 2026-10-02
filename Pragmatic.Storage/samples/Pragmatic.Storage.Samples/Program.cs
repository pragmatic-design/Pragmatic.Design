using Pragmatic.Storage.Samples;

Console.WriteLine("=== Pragmatic.Storage Samples ===\n");

// Self-contained samples (run real local-disk / in-memory demonstrations).
await SaveAndDeleteSample.RunAsync();
await GetAndExistsSample.RunAsync();
await MaxFileSizeSample.RunAsync();
await PathTraversalSample.RunAsync();
MimeTypesSample.Run();
await DependencyInjectionSample.RunAsync();
PragmaticBuilderStorageSample.Run();

// Cloud-provider samples: setup-only (require live Azure/AWS credentials).
// These compile and show real configuration/wiring, but do not execute network calls.
AzureBlobStorageSample.Describe();
S3StorageSample.Describe();

Console.WriteLine("\n=== All samples completed ===");
