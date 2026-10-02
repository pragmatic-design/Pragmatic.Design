using Pragmatic.Patch.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Patch Samples                          ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1. Optional<T> tri-state API: Undefined, Null, Value
OptionalApiSample.Run();

// 2. Generated ApplyTo: partial update, null clearing, exclusions, ModifiedProperties
PatchApplySample.Run();

// 3. Endpoint patterns: Minimal API, JSON behavior, Pragmatic.Endpoints
EndpointPatternSample.Run();

// ── Advanced ─────────────────────────────────────────────────────────────────

// 4. JSON deserialization: tri-state from HTTP body (partial, null, empty, full)
JsonDeserializationSample.Run();

// 5. Private setter: SetXxx() resolution, value type safety
PrivateSetterSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
