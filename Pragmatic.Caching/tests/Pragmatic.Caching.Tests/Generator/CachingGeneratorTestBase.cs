// Pragmatic.Caching.Tests - Generator Test Base
// Thin wrapper over shared GeneratorTestHelper with Caching-specific references.

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.Caching.Attributes;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Caching.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator tests.
///     Provides helper methods to run the generator and verify output.
/// </summary>
public abstract class CachingGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the provided source code.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetCachingReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
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
    ///     Gets generator-specific diagnostics (PRAG1700-1799).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG17");
    }

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
    {
        return GeneratorTestHelper.HasDiagnostic(result, diagnosticId);
    }

    /// <summary>
    ///     Checks if there are no generated files.
    /// </summary>
    protected static bool HasNoGeneratedFiles(SourceGenRunResult result)
    {
        return !result.GeneratedTrees.Any();
    }

    private static MetadataReference[] GetCachingReferences()
    {
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        var references = new List<MetadataReference>
        {
            GeneratorTestHelper.FromType<CacheableAttribute>(),
            // Pragmatic.Abstractions types (IError is now in Pragmatic.Abstractions)
            GeneratorTestHelper.FromType<IError>(),
            // Pragmatic.Result types (Result<T,TError>, VoidResult<T>)
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
            MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Collections.Immutable.dll")),
            // System.Uri is type-forwarded to System.Private.Uri — the generated cache-key code
            // references it, so the test compilation needs the forwarded assembly explicitly.
            MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location)
        };

        return references.ToArray();
    }
}