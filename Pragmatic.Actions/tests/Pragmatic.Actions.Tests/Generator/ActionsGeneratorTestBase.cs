using System.IO;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Base class for PragmaticSourceGenerator tests.
///     Provides helper methods to run the generator and verify output.
/// </summary>
public abstract class ActionsGeneratorTestBase
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the provided source code.
    /// </summary>
    protected static SourceGenRunResult RunGenerator(string source)
    {
        var references = GetActionsReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with Composition referenced (enables metadata generation).
    /// </summary>
    protected static SourceGenRunResult RunGeneratorWithComposition(string source)
    {
        var references = GetActionsReferences()
            .Concat(GetCompositionReferences())
            .ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Runs with both Composition and EF Core referenced: metadata generation, on an action that
    ///     can declare a <c>DbContext</c>.
    /// </summary>
    /// <remarks>
    ///     What a package's metadata says about a boundary-keyed dependency needs both reference sets
    ///     at once; with either alone, no test can fail on it.
    /// </remarks>
    protected static SourceGenRunResult RunGeneratorWithCompositionAndPersistence(string source)
    {
        var references = GetActionsReferences()
            .Concat(GetCompositionReferences())
            .Concat(GetPersistenceReferences())
            .ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Gets the generated source for a specific hint name.
    /// </summary>
    protected static string? GetGeneratedSource(SourceGenRunResult result, string hintNameContains)
        => GeneratorTestHelper.GetGeneratedSource(result, hintNameContains);

    /// <summary>
    ///     Gets the combined boundary output (Definition + Local) for assertions.
    ///     The boundary template splits into Definition (interfaces + DI) and Local (implementation).
    /// </summary>
    protected static string? GetBoundarySource(SourceGenRunResult result)
    {
        var definition = GeneratorTestHelper.GetGeneratedSource(result, "Definition");
        var local = GeneratorTestHelper.GetGeneratedSource(result, "Local");
        if (definition is null && local is null) return null;
        return (definition ?? "") + "\n" + (local ?? "");
    }

    /// <summary>
    ///     Gets all generated sources as a dictionary for inspection.
    /// </summary>
    protected static Dictionary<string, string> GetGeneratedSourcesAsDictionary(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);

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
    ///     Gets generator-specific diagnostics (PRAG0400-0449).
    /// </summary>
    protected static IEnumerable<Diagnostic> GetGeneratorDiagnostics(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG04");

    /// <summary>
    ///     Checks if a specific diagnostic ID was emitted.
    /// </summary>
    protected static bool HasDiagnostic(SourceGenRunResult result, string diagnosticId)
        => GeneratorTestHelper.HasDiagnostic(result, diagnosticId);

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with Persistence referenced (enables AutoAction tests).
    /// </summary>
    protected static SourceGenRunResult RunGeneratorWithPersistence(string source)
    {
        var references = GetActionsReferences()
            .Concat(GetPersistenceReferences())
            .ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with Persistence entity/repository references (enables LoadEntity tests).
    /// </summary>
    protected static SourceGenRunResult RunGeneratorWithEntities(string source)
    {
        var references = GetActionsAndEntityReferences();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    /// <summary>The reference set of <see cref="RunGeneratorWithEntities" />, for a test that adds to it.</summary>
    protected static MetadataReference[] GetActionsAndEntityReferences()
        => GetActionsReferences().Concat(GetEntityReferences()).ToArray();

    private static MetadataReference[] GetActionsReferences()
    {
        return new[]
        {
            // Pragmatic.Actions runtime types (includes Invoker base classes)
            GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
            // Pragmatic.Abstractions types (IError is now in Pragmatic.Abstractions)
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            // Pragmatic.Result types (Result<T>, VoidResult<T>)
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
            // Pragmatic.Ensure types
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
            // Microsoft.Extensions.DependencyInjection.Abstractions (IServiceCollection)
            GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            // Microsoft.Extensions.DependencyInjection (ServiceCollectionServiceExtensions)
            GeneratorTestHelper.FromTypeAssembly(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
            // Microsoft.Extensions.Logging.Abstractions (ILogger)
            GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            // Pragmatic.Authorization (ResourcePolicy + [RequirePolicy<T>]) — for policy diagnostics.
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Authorization.Policy.ResourcePolicy)),
            // Pragmatic.Identity (ICurrentUser) — required by ResourcePolicy.Evaluate.
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Identity.ICurrentUser)),
        };
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with Remote boundary HTTP references.
    ///     Use for tests that generate [RemoteBoundary] code (HttpClient, JSON serialization).
    /// </summary>
    protected static SourceGenRunResult RunGeneratorWithRemote(string source)
    {
        var references = GetActionsReferences()
            .Concat(GetHttpReferences())
            .ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    private static MetadataReference[] GetHttpReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var dlls = new[]
        {
            "System.Net.Http.dll", "System.Net.Http.Json.dll", "System.Text.Json.dll",
            "System.Private.Uri.dll", "System.Net.Primitives.dll"
        };

        var refs = new List<MetadataReference>();
        foreach (var dll in dlls)
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        // Microsoft.Extensions.Http (IHttpClientFactory)
        refs.Add(GeneratorTestHelper.FromTypeAssembly(typeof(System.Net.Http.IHttpClientFactory)));
        return refs.ToArray();
    }

    private static MetadataReference[] GetEntityReferences()
    {
        return new[]
        {
            // Pragmatic.Persistence (IEntity, IReadRepository<T>)
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            // Pragmatic.Persistence runtime (SoftDeleteAttribute, IQueryFilterToggle, etc.)
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
            // Pragmatic.Actions LoadEntity attribute + EntityNotFoundError
            GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.LoadEntityAttribute<object>>(),
            // Pragmatic.Specification (needed by IReadRepository)
            GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
            // System.Linq.Queryable: a declared specification gets generated queryable extensions
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Queryable)),
            // Microsoft.EntityFrameworkCore (needed for IgnoreQueryFilters in restore mutations)
            GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        };
    }

    private static MetadataReference[] GetPersistenceReferences()
    {
        return new[]
        {
            // Pragmatic.Persistence (QueryAttribute, IQueryExecutor, PagedResult)
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Query.Executors.IQueryExecutor>(),
            // Microsoft.EntityFrameworkCore (DbContext, DbSet<T>)
            GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
        };
    }

    /// <summary>
    ///     Runs the PragmaticSourceGenerator with Resilience referenced (enables [ResiliencePolicy] tests).
    /// </summary>
    protected static SourceGenRunResult RunGeneratorWithResilience(string source)
    {
        var references = GetActionsReferences()
            .Concat(GetResilienceReferences())
            .ToArray();
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    private static MetadataReference[] GetCompositionReferences()
    {
        return new[]
        {
            // Pragmatic.Composition (PragmaticMetadataAttribute, MetadataCategory)
            GeneratorTestHelper.FromType<Pragmatic.Composition.Attributes.PragmaticMetadataAttribute>(),
        };
    }

    private static MetadataReference[] GetResilienceReferences()
    {
        return new[]
        {
            // Pragmatic.Resilience (ResiliencePolicyAttribute, IResiliencePipelineProvider, ResilienceContext)
            GeneratorTestHelper.FromType<Pragmatic.Resilience.Attributes.ResiliencePolicyAttribute>(),
        };
    }
}
