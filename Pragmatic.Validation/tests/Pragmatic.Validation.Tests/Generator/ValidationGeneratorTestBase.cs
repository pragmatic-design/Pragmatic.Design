// Pragmatic.Validation.Tests - Generator Test Base
// Thin wrapper over shared GeneratorTestHelper with Validation-specific references.

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator tests.
///     Provides helper methods to run the generator and verify output.
/// </summary>
public abstract class ValidationGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the provided source code.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetValidationReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name.
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
    {
        return GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);
    }

    /// <summary>
    ///     Gets all generated sources as a dictionary for snapshot testing.
    /// </summary>
    protected static Dictionary<string, string> GetGeneratedSourcesAsDictionary(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
    }

    /// <summary>
    ///     Checks if the compilation has any errors after generation.
    /// </summary>
    protected static bool HasCompilationErrors(SourceGenRunResult result)
    {
        return GeneratorTestHelper.HasCompilationErrors(result);
    }

    /// <summary>
    ///     Gets compilation errors after generation.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetCompilationErrors(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetCompilationErrors(result);
    }

    /// <summary>
    ///     Gets compilation warnings after generation.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetCompilationWarnings(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetCompilationWarnings(result);
    }

    /// <summary>
    ///     Gets generator-specific diagnostics (PRAG0200-0299).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG02");
    }

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
    {
        return GeneratorTestHelper.HasDiagnostic(result, diagnosticId);
    }

    /// <summary>
    ///     Gets all diagnostics with a specific ID.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetDiagnosticsById(SourceGenRunResult result, string diagnosticId)
    {
        return GeneratorTestHelper.GetDiagnosticsById(result, diagnosticId);
    }

    /// <summary>
    ///     Checks if there are no generated files (generator skipped due to error).
    /// </summary>
    protected static bool HasNoGeneratedFiles(SourceGenRunResult result)
    {
        return !result.GeneratedTrees.Any();
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with additional metadata references.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source, params MetadataReference[] additionalReferences)
    {
        var references = GetValidationReferences()
            .Concat(additionalReferences)
            .ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Emits the compilation the generator produced and loads it, so a test can run the generated
    ///     validator instead of reading it.
    /// </summary>
    protected static Assembly EmitAndLoad(SourceGenRunResult result)
    {
        using var image = new MemoryStream();
        var emit = result.OutputCompilation.Emit(image);
        emit.Success.Should().BeTrue(string.Join("\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return Assembly.Load(image.ToArray());
    }

    private static MetadataReference[] GetValidationReferences()
    {
        var references = new List<MetadataReference>
        {
            GeneratorTestHelper.FromType<ValidationAttribute>(),
            // Pragmatic.Abstractions types (IError is now in Pragmatic.Abstractions)
            GeneratorTestHelper.FromType<IError>(),
            // Pragmatic.Result types (Result<T,TError>, VoidResult<T>)
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
            MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location)
        };

        return references.ToArray();
    }
}