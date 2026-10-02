using Pragmatic.Specification.Samples.Scenarios;

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║           Pragmatic.Specification Samples                ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Register all samples
ISample[] samples =
[
    new BasicUsageSample(),
    new CompositionSample(),
    new DynamicFilteringSample(),
    new TestingSpecificationsSample(),
    new QueryableSample(),
    new OrderSpecificationsSample(),
    new ComplexBusinessRuleSample(),
    new SentinelAndEdgeCaseSample()
];

// Run each sample
foreach (var sample in samples)
{
    Console.WriteLine($"┌─── {sample.Name} ───");
    Console.WriteLine($"│ {sample.Description}");
    Console.WriteLine("└" + new string('─', 50));
    Console.WriteLine();

    sample.Run();

    Console.WriteLine();
    Console.WriteLine(new string('─', 60));
    Console.WriteLine();
}

Console.WriteLine("All samples completed.");
