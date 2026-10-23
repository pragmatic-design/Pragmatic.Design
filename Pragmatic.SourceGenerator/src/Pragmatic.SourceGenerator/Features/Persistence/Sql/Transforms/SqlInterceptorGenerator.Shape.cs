using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The shadow binding: with Pragmatic's entities and no EF Core, the members their shape declares (<see cref="PragmaticShape" />) are
///     emitted, and the analysis runs on the compilation that has them. The incremental call sites were bound without them (a chain reading
///     <c>r.Property</c> did not bind at all), so the chains are bound again there, with the entities, the declared queries, the DTOs and the
///     functions read from that compilation.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    /// <summary>The Sql engine's bridge, as <c>FeatureDetector.HasPersistenceSql</c> recognizes it.</summary>
    private const string SqlBridgeFilters = "Pragmatic.Persistence.Sql.Pragmatic.PragmaticSqlFilters";

    private static bool WithPragmaticShape(SourceProductionContext spc, ref Compilation compilation, ref ImmutableArray<CallSite?> calls, ref ImmutableArray<INamedTypeSymbol> entityTypes,
        ref ImmutableArray<IMethodSymbol> queries, ref ImmutableArray<INamedTypeSymbol> dtos, ref ImmutableArray<IMethodSymbol> functionMethods)
    {
        if (PragmaticShape.DeclaredByPragmatic(compilation))
        {
            ReportHiddenShape(spc, compilation);
            return false;
        }

        // The engine choice itself is reported by Pragmatic's generator (PRAG0743, PRAG0744), which sees the module's metadata too.
        var engines = PragmaticEngines.Of(compilation);
        var shapes = PragmaticShape.For(compilation, PragmaticEngine.Sql);
        // With the engine's bridge referenced the entities' shape is Pragmatic's own (EntityTraitsTemplate and the others emit it for every
        // engine): this copy is only what the chains are bound against here, and is not emitted a second time.
        var boundOnly = compilation.GetTypeByMetadataName(SqlBridgeFilters) is not null
            ? new HashSet<string>(shapes.Select(s => s.Hint), StringComparer.Ordinal)
            : [];
        // Pragmatic's read repository per entity, as chains this generator binds and intercepts like the application's (PragmaticReadRepository).
        // Their declared queries' shape too (PragmaticQuery), which the repository's RunAsync dispatches on; their grids with them.
        var repositories = new List<(string Hint, string Source)>();
        if (PragmaticReadRepository.Applies(compilation))
        {
            var declared = PragmaticQuery.Read(compilation, PragmaticEngine.Sql);
            foreach (var query in declared)
            {
                shapes.Add(query.Shape());
                if (query.Unsupported is { } why)
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.QueryNotCompiled, query.Type.Locations.FirstOrDefault() ?? Location.None, query.Type.Name, why));
            }

            var served = AllTypes(compilation.GlobalNamespace).Where(t => t.DeclaringSyntaxReferences.Length > 0 && PragmaticShape.IsPragmaticEntity(t) && engines.Serves(t, PragmaticEngine.Sql)).ToList();
            shapes.AddRange(PragmaticRegistration.Sql.For(compilation, served)); // each boundary's unit of work and repositories, keyed by the boundary
            foreach (var entity in served)
            {
                if (PragmaticReadRepository.For(entity, declared.Where(q => SymbolEqualityComparer.Default.Equals(q.Entity, entity)).ToList()) is { } repository)
                    repositories.Add(repository);
                if (PragmaticHierarchy.For(entity, "Pragmatic.Persistence.Sql", "global::Pragmatic.Persistence.Sql.SqlSession", "global::Pragmatic.Persistence.Sql.SqlHierarchy.KeysAsync") is { } hierarchy)
                    repositories.Add(hierarchy); // its chains are bound and intercepted, as a repository's
            }

            // Their grid adapters (PragmaticGrid), whose LoadAsync is a chain bound and intercepted the same way.
            foreach (var grid in PragmaticGrid.Read(compilation, PragmaticEngine.Sql))
            {
                repositories.Add(grid.Source());
                foreach (var omitted in grid.Omitted)
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.GridPartlyCompiled, grid.Type.Locations.FirstOrDefault() ?? Location.None, grid.Type.Name, omitted));
            }
        }

        if (shapes.Count == 0 && repositories.Count == 0)
            return false;
        var options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var written = compilation.SyntaxTrees.ToList();
        var trees = new List<SyntaxTree>();
        foreach (var (hint, text) in shapes.Concat(repositories))
        {
            if (!boundOnly.Contains(hint))
                spc.AddSource(hint, text);
            var tree = CSharpSyntaxTree.ParseText(text, options, hint, cancellationToken: spc.CancellationToken);
            trees.Add(tree);
            if (repositories.Any(r => r.Hint == hint))
                written.Add(tree); // its chains are bound and intercepted with the application's
        }

        compilation = compilation.AddSyntaxTrees(trees);
        entityTypes = Rebound(entityTypes, compilation);
        queries = Rebound(queries, compilation);
        dtos = Rebound(dtos, compilation);
        functionMethods = Rebound(functionMethods, compilation);
        calls = BindAgain(written, compilation, spc.CancellationToken);
        return true;
    }

    /// <summary>
    ///     With EF Core referenced, Pragmatic's generator declares the entities' members and this one cannot see them: a chain reading them
    ///     does not bind here and would run nothing. Each operator whose receiver is a SqlQuery of a Pragmatic entity and that does not bind
    ///     itself is reported (PRAG3029), once per chain: the operators after it do not bind their receiver either.
    /// </summary>
    private static void ReportHiddenShape(SourceProductionContext spc, Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(spc.CancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax access || model.GetSymbolInfo(invocation, spc.CancellationToken).Symbol is not null)
                    continue;
                if (access.Expression is InvocationExpressionSyntax previous && model.GetSymbolInfo(previous, spc.CancellationToken).Symbol is null)
                    continue; // reported at the operator that failed first (its type is the candidate's, so the receiver still reads as a SqlQuery)
                if (model.GetTypeInfo(access.Expression, spc.CancellationToken).Type is INamedTypeSymbol { Name: "SqlQuery", TypeArguments.Length: 1 } receiver
                    && PragmaticShape.IsPragmaticEntity(receiver.TypeArguments[0]))
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.ChainOverHiddenShape, access.Name.GetLocation(), access.Name.Identifier.ValueText, receiver.ToDisplayString()));
            }
        }
    }

    /// <summary>The same declarations in <paramref name="compilation" />, by their documentation id.</summary>
    private static ImmutableArray<T> Rebound<T>(ImmutableArray<T> symbols, Compilation compilation) where T : class, ISymbol
        => symbols.Select(s => DocumentationCommentId.CreateDeclarationId(s) is { } id ? DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) as T : null)
            .Where(s => s is not null).Select(s => s!).ToImmutableArray();

    /// <summary>Every call site of the written files, bound in <paramref name="compilation" />.</summary>
    private static ImmutableArray<CallSite?> BindAgain(List<SyntaxTree> written, Compilation compilation, CancellationToken ct)
    {
        var calls = ImmutableArray.CreateBuilder<CallSite?>();
        foreach (var tree in written)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(ct).DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => i.Expression is MemberAccessExpressionSyntax))
            {
                if (CallSite.TryCreate(invocation, model, ct) is { } call)
                    calls.Add(call);
            }
        }

        return calls.ToImmutable();
    }
}
