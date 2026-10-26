using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;
using ShapeSourceModel = Pragmatic.SourceGenerator.Features.Persistence.Models.ShapeSourceModel;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     Pragmatic's entities as documents, as the Sql feature serves them (<c>SqlInterceptorGenerator.Shape</c>): their shape is Persistence's own
///     (<c>EntityShapeArtifacts</c>), added to a private compilation with what this feature emits for the documents engine (each entity's reader and
///     writer with its filter families, its change detection, its repositories, its declared queries' shape, its grids, each boundary's schema and
///     registration). The incremental call sites were bound without them (a chain reading <c>r.OwnerId</c> did not bind at all), so the chains are
///     bound again there.
/// </summary>
internal static partial class DocumentGenerator
{
    /// <summary>
    ///     The compilation with the shape and the documents engine's sources, and the trees whose chains are bound and intercepted (the source's and
    ///     the repositories'); null when there is no Pragmatic entity the documents engine serves.
    /// </summary>
    private static (Compilation Shadow, List<SyntaxTree> Written)? WithPragmaticShape(SourceProductionContext spc, Compilation compilation, EquatableArray<ShapeSourceModel> shapeSources)
    {
        // The entities' shape is what Persistence emits for every engine a compilation references; with none, nobody declares it.
        if (shapeSources.IsDefaultOrEmpty)
            return null;
        var options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var written = compilation.SyntaxTrees.ToList();
        var shapeTrees = shapeSources.Select(s => CSharpSyntaxTree.ParseText(s.Text, options, s.HintName, cancellationToken: spc.CancellationToken)).ToList();
        // The engine choice itself is reported by Pragmatic's generator (PRAG0743, PRAG0744), which sees the module's metadata too.
        var engines = PragmaticEngines.Of(compilation);
        var entities = PragmaticNames.AllTypes(compilation.GlobalNamespace)
            .Where(t => t.DeclaringSyntaxReferences.Length > 0 && DocModel.IsPragmatic(t) && engines.Serves(t, PragmaticEngine.Documents)).ToList();
        // A trait's child and its list query exist only with the shape: both are read from that one compilation, so the query is the child's.
        var shaped = compilation.AddSyntaxTrees(shapeTrees);
        entities.AddRange(SqlInterceptorGenerator.ShapeDeclaredEntities(compilation, shaped, shapeTrees).Where(t => engines.Serves(t, PragmaticEngine.Documents))); // a trait's child (TraitEngineChildren)
        if (entities.Count == 0)
            return null;

        var trees = new List<SyntaxTree>(shapeTrees);
        SyntaxTree Add(Artifact artifact)
        {
            spc.AddSource(artifact);
            var tree = CSharpSyntaxTree.ParseText(artifact.Text, options, artifact.HintName, cancellationToken: spc.CancellationToken);
            trees.Add(tree);
            return tree;
        }

        SyntaxTree AddText((string Hint, string Source) source) => Add(Artifact.FromString(source.Hint, source.Source));

        var diagnostics = new List<Diagnostic>();
        // A document keeps a relation as its key: a load with Include has no navigation to read (owner's decision of 2026-10-25).
        foreach (var load in LoadIncludes.Read(compilation).Where(l => engines.Serves(l.Entity, PragmaticEngine.Documents)))
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.LoadIncludeNotSupported, load.Location, load.Attribute, load.Entity.Name, load.Operation.Name));
        if (DocReadRepository.Applies(compilation))
        {
            // The documents as the shaped entities store them: what the declared queries and the grids order by and project from.
            var models = new Dictionary<string, DocModel>();
            foreach (var entity in entities)
            {
                if (shaped.GetTypeByMetadataName(entity.ToDisplayString()) is { } type && DocModel.Read(type, []) is { } read)
                    models[entity.ToDisplayString()] = read; // its diagnostics are reported once, below
            }

            var declared = PragmaticQuery.Read(compilation, PragmaticEngine.Documents);
            declared.AddRange(PragmaticQuery.Read(shaped, PragmaticEngine.Documents).Where(q => q.Type.DeclaringSyntaxReferences.Any(r => shapeTrees.Contains(r.SyntaxTree))));
            var queries = DeclaredQueries(compilation, declared, models, diagnostics, AddText);
            foreach (var schema in PragmaticDocSchema.For(compilation, entities, models, queries.Select(q => q.Query).ToList()))
                Add(new DocSchemaTemplate(schema).RenderOutput()); // each boundary's collections, indexes and Cosmos DB containers (DocSchema)
            foreach (var grid in PragmaticGrid.Read(compilation, PragmaticEngine.Documents))
            {
                if (!models.TryGetValue(grid.Entity.ToDisplayString(), out var model))
                    continue;
                foreach (var omitted in grid.Omitted)
                    diagnostics.Add(Diagnostic.Create(DocDiagnostics.GridPartlyCompiled, grid.Type.Locations.FirstOrDefault() ?? Location.None, grid.Type.Name, omitted));
                written.Add(AddText(DocGrids.Source(grid, model))); // its LoadAsync's chains are bound and intercepted with the application's
            }

            foreach (var registration in PragmaticRegistration.Documents.For(compilation, entities))
                AddText(registration); // each boundary's unit of work and repositories, keyed by the boundary
            foreach (var entity in entities)
            {
                var own = queries.Where(q => SymbolEqualityComparer.Default.Equals(q.Query.Entity, entity)).ToList();
                if (DocReadRepository.For(entity, own) is { } repository)
                    written.Add(Add(new DocReadRepositoryTemplate(repository).RenderOutput())); // its chains are bound and intercepted with the application's
                if (PragmaticHierarchy.For(entity, "Pragmatic.Persistence.Documents", "global::Pragmatic.Persistence.Documents.DocSession", "global::Pragmatic.Persistence.Documents.Execution.DocHierarchy.KeysAsync") is { } hierarchy)
                    written.Add(AddText(hierarchy)); // [GenerateHierarchy]: its chains too
            }
        }

        var shadow = compilation.AddSyntaxTrees(trees);
        var scopeRules = new List<string>();
        var bridged = shadow.GetTypeByMetadataName("Pragmatic.Persistence.Documents.CompiledScopeRules") is not null;
        foreach (var entity in entities)
        {
            if (shadow.GetTypeByMetadataName(entity.ToDisplayString()) is not { } shapedEntity || DocModel.Read(shapedEntity, diagnostics) is not { } model)
                continue;
            var hint = VirtualFolderHints.ForType(entity.Name, "Document", entity.ContainingNamespace.ToDisplayString());
            if (DocTypeEmitter.Model(model, hint, diagnostics, DocFamiliesEmitter.Families(model, shadow, diagnostics)) is not { } typeModel)
                continue;
            spc.AddSource(new DocumentTypeTemplate(typeModel).RenderOutput());
            if (DocTrackingEmitter.Model(model, diagnostics) is { } tracking)
                spc.AddSource(new DocumentTrackingTemplate(tracking).RenderOutput());
            if (bridged)
                scopeRules.AddRange(DocFamiliesEmitter.ScopeRuleRegistrations(model, shadow));
        }

        spc.AddSource(new DocScopeRulesTemplate(scopeRules.ToEquatableArray()).RenderOutput());
        foreach (var diagnostic in diagnostics)
        {
            // A diagnostic on a trait's child falls in a shape file, which Roslyn refuses (the generator's whole output dropped with it). Not
            // silent: a child the engine does not read gets no document type, and the code using it does not compile.
            if (diagnostic.Location.SourceTree is { } tree && !compilation.ContainsSyntaxTree(tree))
                continue;
            spc.ReportDiagnostic(diagnostic);
        }

        return (shadow, written);
    }

    /// <summary>
    ///     The declared queries on the entities the documents engine serves, their shapes emitted (the filters as specifications this generator
    ///     compiles, the sorts as a runtime ordering) with their projections; one the documents engine does not compile is reported (PRAG3108) and
    ///     refused at run time, as on the Sql engine.
    /// </summary>
    private static List<(PragmaticQuery Query, string? Projection)> DeclaredQueries(Compilation compilation, List<PragmaticQuery> declared, Dictionary<string, DocModel> models, List<Diagnostic> diagnostics,
        System.Func<(string Hint, string Source), SyntaxTree> add)
    {
        var queries = new List<(PragmaticQuery, string?)>();
        foreach (var query in declared.Where(q => models.ContainsKey(q.Entity.ToDisplayString())))
        {
            var model = models[query.Entity.ToDisplayString()];
            var projection = DocQueries.Prepare(query, model);
            if (query.Unsupported is { } why)
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.QueryNotCompiled, SqlInterceptorGenerator.ReportableLocation(query.Type, compilation), query.Type.Name, why));
            add(DocQueries.Shape(query, model));
            queries.Add((query, projection));
        }

        return queries;
    }

    /// <summary>Every document call of <paramref name="written" />, bound in <paramref name="shadow" />.</summary>
    private static ImmutableArray<DocCall> BindAgain(List<SyntaxTree> written, Compilation shadow, CancellationToken ct)
    {
        var calls = ImmutableArray.CreateBuilder<DocCall>();
        foreach (var tree in written)
        {
            var model = shadow.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot(ct).DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => i.Expression is MemberAccessExpressionSyntax))
            {
                if (DocCall.TryCreate(invocation, model, ct) is { } call)
                    calls.Add(call);
            }
        }

        return calls.ToImmutable();
    }
}
