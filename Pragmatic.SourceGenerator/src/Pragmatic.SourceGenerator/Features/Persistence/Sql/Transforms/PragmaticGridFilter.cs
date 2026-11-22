using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Pragmatic's grid filters (<c>[GridFilter&lt;TEntity&gt;]</c>) on the entities an engine serves (#946). Pragmatic's generator writes them
///     only with EF Core (its <c>QueryFeature</c> sits behind the same gate as the entities' shape), so the engine writes them, from the same
///     model and the same template (<see cref="GridFilterApplyTemplate" />): <c>ToSpecification()</c>, whose <c>Spec&lt;T&gt;.Where</c> leaves the
///     engine compiles, <c>ToOrdering()</c> and <c>LoadAsync(repository)</c>, with <c>Apply</c> refused. The entity's repository compiles a key
///     for each member a grid filter sorts by (<see cref="SortMembers" />). This file is engine-neutral (read by the documents generator too).
/// </summary>
internal sealed class PragmaticGridFilter
{
    private const string Attribute = "Pragmatic.Persistence.Query.Attributes.GridFilterAttribute";

    private PragmaticGridFilter(INamedTypeSymbol type, INamedTypeSymbol entity, GridFilterModel model)
    {
        Type = type;
        Entity = entity;
        Model = model;
    }

    public INamedTypeSymbol Type { get; }

    public INamedTypeSymbol Entity { get; }

    public GridFilterModel Model { get; }

    /// <summary>The members the filter sorts by, as the ordering names them: one key each in the entity's repository.</summary>
    public IEnumerable<string> SortMembers => Model.SortableProperties.Select(p => p.EffectivePropertyPath);

    /// <summary>The partial grid filters of the compilation on the Pragmatic entities <paramref name="engine" /> serves.</summary>
    public static List<PragmaticGridFilter> Read(Compilation compilation, PragmaticEngine engine, CancellationToken ct)
    {
        var filters = new List<PragmaticGridFilter>();
        var engines = PragmaticEngines.Of(compilation);
        foreach (var type in PragmaticNames.AllTypes(compilation.GlobalNamespace).Where(t => t.DeclaringSyntaxReferences.Length > 0))
        {
            var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass is { IsGenericType: true, TypeArguments.Length: 1 } c
                && c.OriginalDefinition.ToDisplayString().StartsWith(Attribute + "<", System.StringComparison.Ordinal));
            if (attribute?.AttributeClass!.TypeArguments[0] is not INamedTypeSymbol entity || !PragmaticShape.IsPragmaticEntity(entity) || !engines.Serves(entity, engine))
                continue;
            // As EF Core's half: a grid filter that is not partial gets nothing.
            if (!type.DeclaringSyntaxReferences.Any(r => GridFilterTransform.IsPartialType(r.GetSyntax(ct))))
                continue;
            filters.Add(new PragmaticGridFilter(type, entity, GridFilterTransform.FromSymbol(type, entity, ct)));
        }

        return filters;
    }

    /// <summary>The grid filter's half on the engine (hint name, text).</summary>
    public (string Hint, string Source) SourceText(GridFilterEngineModel engine)
    {
        var template = new GridFilterApplyTemplate(Model, engine);
        var artifact = template.RenderOutput();
        return (artifact.HintName, artifact.Text);
    }
}
