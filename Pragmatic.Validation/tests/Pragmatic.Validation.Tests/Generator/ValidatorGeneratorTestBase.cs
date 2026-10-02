// Pragmatic.Validation.Tests - Validator Generator Test Base
// Thin wrapper over shared GeneratorTestHelper with Validation-specific references.
// This class includes additional DI references for testing validator registration output.

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Base class for ValidatorGenerator tests.
///     Runs the unified PragmaticSourceGenerator which handles both ISyncValidator and DI registration.
///     Includes DI references required for testing validator registration output.
/// </summary>
public abstract class ValidatorGeneratorTestBase
{
    /// <summary>
    ///     Runs the unified PragmaticSourceGenerator on the provided source code.
    ///     The generator handles both ISyncValidator generation and DI registration.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetValidatorReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name.
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    /// <summary>
    ///     Gets all generated sources as a dictionary for inspection.
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
    ///     Gets generator-specific diagnostics (PRAG02xx for Validation).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG02");

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    /// <summary>
    ///     Checks if there are no generated files (generator skipped due to error).
    /// </summary>
    protected static bool HasNoGeneratedFiles(SourceGenRunResult result)
        => !result.GeneratedTrees.Any();

    private static MetadataReference[] GetValidatorReferences()
    {
        return
        [
            // Validation-specific references
            GeneratorTestHelper.FromType<ValidationAttribute>(),
            // Pragmatic.Abstractions types (IError is now in Pragmatic.Abstractions)
            GeneratorTestHelper.FromType<IError>(),
            // Pragmatic.Result types (Result<T,TError>, VoidResult<T>)
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
            MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location),
            // DI and Options - required for ValidatorGenerator output
            MetadataReference.CreateFromFile(typeof(ServiceLifetime).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IOptions<>).Assembly.Location)
        ];
    }
}
