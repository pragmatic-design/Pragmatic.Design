using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Jobs.Tests.Generator;

public abstract class JobsGeneratorTestBase
{
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetJobsReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    protected static Dictionary<string, string> GetAllGeneratedSources(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);

    protected static IEnumerable<Diagnostic> GetCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result);

    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG25");

    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    private static MetadataReference[] GetJobsReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<Pragmatic.Jobs.IJob>(),
            GeneratorTestHelper.FromType<Pragmatic.Jobs.Attributes.JobAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Resilience.Attributes.RetryAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.Abstractions.NullLogger>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Composition.Metadata.MetadataCategory)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Json.JsonSerializer)),
            GeneratorTestHelper.FromType<Microsoft.Extensions.Hosting.IHostedService>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.Options.IOptions<>)),
            // Required for the generated invoker (ActivitySource/Meter) and registry
            // (PragmaticJsonOptions) to actually compile — asserted via GetCompilationErrors.
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Diagnostics.ActivitySource)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Serialization.PragmaticJsonOptions)),
        ];
    }
}
