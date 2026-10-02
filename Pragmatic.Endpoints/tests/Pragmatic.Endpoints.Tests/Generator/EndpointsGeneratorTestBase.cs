// Pragmatic.Endpoints.Tests - Generator Test Base
// Thin wrapper over shared GeneratorTestHelper with Endpoints-specific references.

using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator tests.
///     Provides helper methods to run the generator and verify output.
/// </summary>
public abstract class EndpointsGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the provided source code.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetEndpointsReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Runs the generator with the Endpoints references plus the ones the test adds — typically an
    ///     assembly built with <see cref="GeneratorTestHelper.CompileReference" />.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source, params MetadataReference[] additionalReferences)
    {
        var references = GetEndpointsReferences().Concat(additionalReferences).ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name.
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    /// <summary>
    ///     Gets all generated sources as a dictionary for snapshot testing.
    /// </summary>
    protected static Dictionary<string, string> GetGeneratedSourcesAsDictionary(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

    /// <summary>
    ///     Gets all generated sources as a dictionary for Verify snapshot verification.
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
    ///     Gets compilation warnings after generation.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetCompilationWarnings(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationWarnings(result);

    /// <summary>
    ///     Gets generator-specific diagnostics (PRAG0500-0599).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG05");

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    /// <summary>
    ///     Gets all diagnostics with a specific ID.
    /// </summary>
    protected static IEnumerable<Diagnostic> GetDiagnosticsById(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.GetDiagnosticsById(result, diagnosticId);

    /// <summary>
    ///     Checks if there are no generated files (generator skipped due to error).
    /// </summary>
    protected static bool HasNoGeneratedFiles(SourceGenRunResult result)
        => !result.GeneratedTrees.Any();

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with Identity.AspNetCore references.
    ///     Required for testing PragmaticPermissionRequirement bridge generation.
    /// </summary>
    protected static SourceGenRunResult RunGeneratorWithIdentity(string source)
    {
        var references = GetEndpointsReferences().ToList();
        references.AddRange(GetIdentityAspNetCoreReferences());
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references.ToArray());
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with additional Persistence references.
    ///     Required for [Query&lt;&gt;] + [Endpoint] tests that need Persistence references.
    /// </summary>
    protected static SourceGenRunResult RunGeneratorWithPersistence(string source)
    {
        var references = GetEndpointsReferences().ToList();
        references.AddRange(GetPersistenceReferences());
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references.ToArray());
    }

    /// <summary>
    ///     Runs the generator with the pieces <c>[RecordAccess]</c> needs: the attribute, and the audit
    ///     trail the generated handler writes to.
    /// </summary>
    /// <remarks>
    ///     Both, always — including for the test that asserts nothing is recorded without the attribute.
    ///     With the audit package absent that assertion holds whatever the attribute does, which is the
    ///     shape of a test that passes for the wrong reason.
    /// </remarks>
    protected static SourceGenRunResult RunGeneratorWithAudit(string source)
    {
        var references = GetEndpointsReferences().ToList();
        references.AddRange(GetPersistenceReferences());
        references.Add(GeneratorTestHelper.FromType<Pragmatic.Privacy.RecordAccessAttribute>());
        references.Add(GeneratorTestHelper.FromType<Pragmatic.Audit.IAuditTrail>());

        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references.ToArray());
    }

    private static MetadataReference[] GetEndpointsReferences()
    {
        var references = new List<MetadataReference>
        {
            // Pragmatic.Endpoints runtime types
            GeneratorTestHelper.FromType<Pragmatic.Endpoints.Attributes.EndpointAttribute>(),
            // Pragmatic.Abstractions types (IError is now in Pragmatic.Abstractions)
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            // Pragmatic.Result types (Result<T,TError>, VoidResult<T>)
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
            // Pragmatic.Actions types (for DomainAction and Mutation endpoints)
            GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Actions.Mutation.Mutation<object>>(),
            // Pragmatic.Validation types (for ISyncValidator detection)
            GeneratorTestHelper.FromType<Pragmatic.Validation.ISyncValidator>(),
            // Pragmatic.Authorization (ResourcePolicy + [RequirePolicy<T>]) — so a policy declared on a
            // query binds, and the generated query endpoint's own policy check can be asserted.
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Authorization.Policy.ResourcePolicy)),
            // ASP.NET Core rate limiting — required so inline [RateLimit(Requests, Window)] policies
            // are generated (the SG suppresses inline generation when this runtime is absent).
            GeneratorTestHelper.FromType<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(),
            // System.Text.Json — the generated JSON context is a JsonSerializerContext. It is not in the
            // shared base references, so without this the context never compiles and any assertion about
            // it can only be made against its text.
            GeneratorTestHelper.FromType<System.Text.Json.JsonSerializerOptions>(),
            GeneratorTestHelper.FromType<System.Text.Json.Serialization.Metadata.JsonTypeInfo>(),
        };

        // Add ASP.NET Core references
        var aspNetReferences = new[]
        {
            "Microsoft.AspNetCore.Mvc.Core",
            "Microsoft.AspNetCore.Mvc.Abstractions",
            "Microsoft.AspNetCore.Http.Abstractions",
            "Microsoft.AspNetCore.Http.Results",
            "Microsoft.AspNetCore.Http",
            "Microsoft.AspNetCore.Http.Extensions",
            "Microsoft.AspNetCore.Http.Features",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Primitives",
            "Microsoft.AspNetCore.Routing",
            "Microsoft.AspNetCore.Routing.Abstractions",
            "Microsoft.AspNetCore.Authorization",
            "Microsoft.AspNetCore.Authorization.Policy",
            "Microsoft.AspNetCore.Metadata",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.DependencyInjection",
            "System.Diagnostics.DiagnosticSource",
            "System.Linq"
        };

        foreach (var assemblyName in aspNetReferences)
        {
            var reference = GeneratorTestHelper.TryGetAssemblyReference(assemblyName);
            if (reference != null)
            {
                references.Add(reference);
            }
        }

        return references.ToArray();
    }

    private static MetadataReference[] GetIdentityAspNetCoreReferences()
    {
        return new MetadataReference[]
        {
            // ⚠️ The permission *handler*, not the requirement. The requirement lives in
            // Pragmatic.Endpoints.AspNetCore, and what makes a compilation "have identity" is the half
            // that evaluates it — which is the half FeatureDetector anchors on too. Naming the
            // requirement here would make this method mean "a type the generated policy names anyway".
            GeneratorTestHelper.FromType<Pragmatic.Identity.Authorization.PragmaticPermissionHandler>(),
        };
    }

    private static MetadataReference[] GetPersistenceReferences()
    {
        return new MetadataReference[]
        {
            // Pragmatic.Persistence (QueryAttribute, IQueryExecutor, PagedResult)
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Query.Executors.IQueryExecutor>(),
            // Pragmatic.Specification — a [Query] on a specification derives a query, and its route is
            // generated here: without this the specification is an unresolved type and nothing derives.
            GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
        };
    }
}
