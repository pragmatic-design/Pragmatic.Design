using Pragmatic.Mapping.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Mapping Samples                        ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Core Mapping ─────────────────────────────────────────────────────────────

// 1. Basic mapping with flattening, concatenation, formatting
BasicMappingSample.Run();

// 2. Nested DTOs and collections
NestedMappingSample.Run();

// 3. Bidirectional mapping (Entity <-> DTO)
BidirectionalMappingSample.Run();

// 4. Struct/record struct mapping for zero-allocation
StructMappingSample.Run();

// 5. EF Core projections (Expression<Func<>>)
ProjectionSample.Run();

// ── Type Handling ────────────────────────────────────────────────────────────

// 6. Type conversions (ToString, Parse, DateTime, Dictionary with DTOs)
TypeConversionSample.Run();

// 7. In-memory Selector (Func<>) for LINQ operations
SelectorSample.Run();

// 8. Nullable handling, collection conversions, circular references
NullableAndAdvancedSample.Run();

// 9. Type combinations (class/record/struct source/target)
TypeCombinationsSample.Run();

// ── Attributes ───────────────────────────────────────────────────────────────

// 10. Advanced attributes (MapIgnore, MapConverter, MapProperty)
AdvancedAttributesSample.Run();

// 11. ApplyTo partial updates, nested MapTo, TargetPath
ApplyToSample.Run();

// 12. BodyOnly variant, MapConstructor, best-match constructor
BodyOnlyAndConstructorSample.Run();

// ── Deep Nesting ─────────────────────────────────────────────────────────────

// 13. Deep nesting (3 levels) and self-referencing with circular ref handling
DeepNestingSample.Run();

// 14. Advanced nesting: 4 levels, nullable intermediates, self-ref round-trip
AdvancedNestingSample.Run();

// ── Real-World Scenarios ─────────────────────────────────────────────────────

// 15. Converter combinations: multiple converters, bidirectional, mixed combos
ConverterCombinationsSample.Run();

// 16. Property name mismatches, multi-level flattening (2-4 levels deep)
RealWorldMappingSample.Run();

// 17. Concatenation, converter on nested paths, bidirectional with renames + TargetPath
CombinedScenariosSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
