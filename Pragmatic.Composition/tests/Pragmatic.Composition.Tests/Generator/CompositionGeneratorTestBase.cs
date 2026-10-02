// Pragmatic.Composition.Tests - Generator Test Base
// Base class for PragmaticSourceGenerator snapshot tests.
// Uses shared GeneratorTestHelper with Composition-specific references.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Hosting;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Composition.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator snapshot tests.
///     Provides helper methods to run the generator and verify output.
/// </summary>
public abstract class CompositionGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the provided source code (Library mode).
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source, params MetadataReference[] extraReferences)
    {
        var references = GetCompositionReferences().Concat(extraReferences).ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator in HOST mode (ConsoleApplication output kind).
    /// </summary>
    protected static SourceGenRunResult RunHostModeGenerator(string source)
    {
        var references = GetCompositionReferences();
        var compilation = CreateCompilation(source, OutputKind.ConsoleApplication, references);
        return RunGeneratorOnCompilation(compilation);
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name.
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    /// <summary>
    ///     Gets all generated sources as a dictionary for snapshot verification.
    /// </summary>
    protected static Dictionary<string, string> GetAllGeneratedSources(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

    /// <summary>
    ///     Checks if the compilation has any errors after generation.
    /// </summary>
    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);

    /// <summary>
    ///     Gets compilation errors after generation.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result);

    /// <summary>
    ///     Gets generator-specific diagnostics (PRAG1400-1499 for DI, PRAG1600-1699 for Composition).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG1");

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    private static MetadataReference[] GetCompositionReferences()
    {
        var references = new List<MetadataReference>
        {
            // Pragmatic.Composition runtime types (attributes, abstractions)
            GeneratorTestHelper.FromType<StartupStepAttribute>(),
            GeneratorTestHelper.FromType<IStartupStep>(),
            GeneratorTestHelper.FromType<ServiceAttribute>(),

            // Pragmatic.Composition.Host — marker type PragmaticBuilder is how the SG
            // detects that the runtime host package is actually available.
            GeneratorTestHelper.FromType<PragmaticBuilder>(),

            // Microsoft.Extensions.DependencyInjection.Abstractions (IServiceCollection)
            GeneratorTestHelper.FromType<IServiceCollection>(),

            // Microsoft.Extensions.Hosting.Abstractions (IHostEnvironment)
            GeneratorTestHelper.FromType<IHostEnvironment>(),

            // Microsoft.Extensions.Configuration.Abstractions (IConfiguration)
            GeneratorTestHelper.FromType<IConfiguration>()
        };

        // ASP.NET Core references needed for HOST mode (may not be available in all environments)
        var aspNetCore = GeneratorTestHelper.TryGetAssemblyReference("Microsoft.AspNetCore.Builder");
        if (aspNetCore != null)
            references.Add(aspNetCore);

        var routingAbstractions = GeneratorTestHelper.TryGetAssemblyReference(
            "Microsoft.AspNetCore.Routing.Abstractions");
        if (routingAbstractions != null)
            references.Add(routingAbstractions);

        var hosting = GeneratorTestHelper.TryGetAssemblyReference("Microsoft.Extensions.Hosting");
        if (hosting != null)
            references.Add(hosting);

        // Pragmatic.Composition.Extensions (for Decorate<> extension method)
        var compositionExtensions = GeneratorTestHelper.TryGetAssemblyReference("Pragmatic.Composition");
        if (compositionExtensions != null)
            references.Add(compositionExtensions);

        return references.ToArray();
    }

    /// <summary>
    ///     Creates a compilation with specific output kind (for testing HOST vs LIBRARY mode).
    /// </summary>
    private static CSharpCompilation CreateCompilation(
        string source,
        OutputKind outputKind,
        MetadataReference[] additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var baseReferences = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.ComponentModel.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "netstandard.dll"))
        };

        baseReferences.AddRange(additionalReferences);

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            baseReferences,
            new CSharpCompilationOptions(outputKind)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator on an existing compilation.
    /// </summary>
    private static SourceGenRunResult RunGeneratorOnCompilation(CSharpCompilation compilation)
    {
        var generator = new PragmaticSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return new SourceGenRunResult(runResult, outputCompilation, diagnostics);
    }
}
