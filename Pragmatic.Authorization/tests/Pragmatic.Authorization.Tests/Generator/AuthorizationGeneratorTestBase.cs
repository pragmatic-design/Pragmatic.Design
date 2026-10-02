// Pragmatic.Authorization.Tests - Generator Test Base
// Thin wrapper over the shared GeneratorTestHelper with the references the Identity/Authorization
// SG feature needs: it only runs when Pragmatic.Authorization is on the compilation
// (FeatureDetector.HasAuthorization looks for PragmaticBuilderAuthorizationExtensions).

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator tests covering the Identity/Authorization feature
///     (permission constants, PermissionRegistry, RoleRegistry, catalog registration).
/// </summary>
public abstract class AuthorizationGeneratorTestBase
{
    /// <summary>Runs the PragmaticSourceGenerator on the provided source code.</summary>
    protected static SourceGenRunResult RunGenerator(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, GetAuthorizationReferences());

    /// <summary>Runs the generator with one more reference — an assembly the source uses.</summary>
    protected static SourceGenRunResult RunGenerator(string source, MetadataReference reference)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, [.. GetAuthorizationReferences(), reference]);

    /// <summary>Gets the generated source whose hint name contains <paramref name="hintNameContains" />.</summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    /// <summary>Gets every generated source, keyed by hint name.</summary>
    protected static Dictionary<string, string> GetGeneratedSourcesAsDictionary(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

    /// <summary>Checks whether the compilation has errors after generation.</summary>
    protected static bool HasCompilationErrors(SourceGenRunResult result)
        => GeneratorTestHelper.HasCompilationErrors(result);

    /// <summary>Gets Identity/Authorization generator diagnostics (PRAG1000-1099).</summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG10");

    /// <summary>Checks whether a specific diagnostic ID was emitted.</summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    /// <summary>The references the Identity/Authorization feature needs — also for a referenced module.</summary>
    protected static MetadataReference[] GetAuthorizationReferences() =>
    [
        // PermissionAttribute / IRole / IResourceAuthorizer live in Pragmatic.Abstractions.
        GeneratorTestHelper.FromTypeAssembly(typeof(PermissionAttribute)),
        // The feature flag: FeatureDetector looks for this type to enable the Identity feature.
        GeneratorTestHelper.FromTypeAssembly(typeof(PragmaticBuilderAuthorizationExtensions)),
        // Microsoft.Extensions.DependencyInjection.Abstractions (IServiceCollection) — the generated
        // catalog registration extends it.
        GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
        GeneratorTestHelper.FromTypeAssembly(
            typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
    ];
}
