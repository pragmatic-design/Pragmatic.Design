using Pragmatic.Result.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Result Samples                         ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Core Pattern ─────────────────────────────────────────────────────────────

// 1-3. Basic Result, Railway-Oriented (Map/Bind), Multi-Error
BasicResultSample.Run();
RailwayOrientedSample.Run();
MultiErrorSample.Run();

// 4-5. VoidResult, Maybe<T>
VoidResultSample.Run();
MaybeSample.Run();

// ── Patterns ─────────────────────────────────────────────────────────────────

// 6-8. Deconstruction, TryCatch, Async Pipeline
DeconstructSample.Run();
TryCatchSample.Run();
AsyncPipelineSample.Run();

// ── Advanced ─────────────────────────────────────────────────────────────────

// 9. Side effects: Tap, OnSuccess, OnFailure
SideEffectsSample.Run();

// 10. Recovery: FromNullable, Ensure, MapError
RecoverySample.Run();

// 11. Advanced APIs: Recover, OrElse, CollectAll, Combine, Try, FromNullable
AdvancedApisSample.Run();

// 12. JSON serialization round-trip via the typed ResultJsonConverter<T, E>
JsonSerializationSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
