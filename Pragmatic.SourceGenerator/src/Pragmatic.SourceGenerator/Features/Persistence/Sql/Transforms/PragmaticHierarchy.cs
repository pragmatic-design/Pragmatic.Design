using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Pragmatic's <c>[GenerateHierarchy]</c> on an engine (<c>HierarchyQueryTemplate</c> emits them over a <c>DbContext</c>, with EF Core only):
///     per tree, <c>GetDescendantsBy{Navigation}Async</c> and <c>GetAncestorsBy{Navigation}Async</c>. The engine's runtime walks the parent keys
///     (its <c>keys</c> method, bounded in depth, the start left out, no filter, as Pragmatic's CTE); the rows are then read with a chain on the
///     session, so the caller's filters apply to them. Engine-neutral, linked by the documents generator.
/// </summary>
internal static class PragmaticHierarchy
{
    /// <summary>The entity's hierarchy methods (hint name, text); null when it declares no tree.</summary>
    public static (string Hint, string Source)? For(INamedTypeSymbol entity, string package, string session, string keys)
    {
        var trees = PragmaticRelations.Trees(entity).Select(t => new HierarchyTreeModel(t.Navigation, t.ForeignKey)).ToEquatableArray();
        if (trees.Count == 0)
            return null;
        var model = new HierarchyQueriesModel(
            VirtualFolderHints.ForType(entity.Name, "SqlHierarchy", entity.ContainingNamespace.ToDisplayString()),
            package,
            entity.ContainingNamespace.IsGlobalNamespace ? null : entity.ContainingNamespace.ToDisplayString(),
            entity.Name,
            "global::" + entity.ToDisplayString(),
            session,
            keys,
            trees);
        return (model.Hint, new HierarchyQueriesTemplate(model).ToSourceText().ToString());
    }
}
