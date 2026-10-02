using Pragmatic.Ensure.Samples.Samples;

Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Ensure Samples                         ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// 1. Constructor guards: null, empty, range
ConstructorGuardsSample.Run();

// 2. Conditional logic with Is-pattern
ConditionalLogicSample.Run();

// 3. Domain entity invariant enforcement
DomainEntitySample.Run();

// 4. String format + numeric guards
FormatAndNumericSample.Run();

// 5. Collection, date, misc guards + fluent return
CollectionAndMiscSample.Run();

// 6. ThrowIfNull two-parameter tuple overload
ThrowIfNullTupleSample.Run();

// 7. Check.* Result API — Bind chaining + string/collection guards
CheckBindSample.Run();

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("All samples completed successfully!");
Console.WriteLine();
