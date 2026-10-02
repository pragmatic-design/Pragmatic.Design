using Pragmatic.Documents.Csv.Samples.Samples;

Console.WriteLine("=== Pragmatic.Documents.Csv Samples ===\n");

// All samples round-trip through in-memory byte arrays — no file system side effects.
// Each scenario prints both the produced CSV text and the parsed-back shape so
// you can see the encoder/decoder behavior end-to-end.

BasicRoundTripSample.Run();
LocaleSample.Run();
FormulaInjectionSample.Run();
QuotingAndEdgeCasesSample.Run();

Console.WriteLine("\n=== All samples completed. ===");
