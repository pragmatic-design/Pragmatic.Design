using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     The shape of a declared query of Pragmatic's on an engine: the interface, <c>Apply</c> (refused: the engine reads the declaration, not an
///     <c>IQueryable</c>), <c>ToSpecification()</c> (the filters as <c>Spec&lt;T&gt;.Where</c> leaves) and the engine's ordering.
/// </summary>
internal sealed class QueryShapeTemplate(QueryShapeModel model) : CSharpTemplate
{
    protected override string? GeneratorName => model.Package;

    protected override string? SourceInfo => $"the shape of {model.TypeName}, a declared query of Pragmatic's on the {model.Engine}";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var entity = model.EntityType;
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.TypeName} : global::Pragmatic.Persistence.Query.Interfaces.{(model.Paged ? "IPagedQuery" : "IQuery")}<{entity}, {model.ResultType}>");
        AppendLine("{");
        AppendLine($"    public global::System.Linq.IQueryable<{entity}> Apply(global::System.Linq.IQueryable<{entity}> query)");
        AppendLine($"        => throw new global::System.NotSupportedException(\"{model.TypeName} runs on the {model.Engine}, which reads the declaration (IReadRepository.RunAsync), not an IQueryable.\");");
        if (model.Compiled)
        {
            AppendLine();
            AppendLine($"    /// <summary>The filters of {model.TypeName}, as Pragmatic composes them.</summary>");
            AppendLine($"    public global::Pragmatic.Specification.Specification<{entity}> ToSpecification()");
            AppendLine("    {");
            AppendLine($"        var spec = global::Pragmatic.Specification.Spec<{entity}>.True;");
            foreach (var c in model.Conditions)
                AppendLine($"        {(c.Guard is null ? "" : $"if ({c.Guard}) ")}spec = spec & global::Pragmatic.Specification.Spec<{entity}>.Where(e => {c.Condition});");
            foreach (var group in model.Groups)
                RenderGroup(group, "spec", parentOr: null, "        ");
            AppendLine("        return spec;");
            AppendLine("    }");
            AppendLine();
            if (model.Ordering is not null)
                RenderOrdering(model.Ordering);
        }

        AppendLine("}");
    }

    private int _groupCount;

    /// <summary>
    ///     A filter object (<c>[ComplexFilter]</c>, or a <c>[FilterGroup]</c> inside one) as Pragmatic's <c>[FilterDto]</c> composes it: null until a
    ///     member is set, each set member joined with the group's AND or OR, then joined into <paramref name="target" />: the query's specification
    ///     with AND (<paramref name="parentOr" /> null), else the enclosing group with that group's logic.
    /// </summary>
    private void RenderGroup(QueryFilterGroupModel group, string target, bool? parentOr, string indent)
    {
        var entity = model.EntityType;
        var specification = $"global::Pragmatic.Specification.Specification<{entity}>";
        var name = $"__group{_groupCount++}";
        var join = group.Or ? "|" : "&";
        AppendLine($"{indent}if ({group.Guard})");
        AppendLine($"{indent}{{");
        AppendLine($"{indent}    {specification}? {name} = null;");
        foreach (var c in group.Conditions)
        {
            AppendLine($"{indent}    {(c.Guard is null ? "" : $"if ({c.Guard}) ")}{{");
            AppendLine($"{indent}        var __leaf = global::Pragmatic.Specification.Spec<{entity}>.Where(e => {c.Condition});");
            AppendLine($"{indent}        {name} = {name} is null ? __leaf : ({name} {join} __leaf);");
            AppendLine($"{indent}    }}");
        }

        foreach (var nested in group.Groups)
            RenderGroup(nested, name, group.Or, indent + "    ");
        AppendLine(parentOr is null
            ? $"{indent}    if ({name} is not null) {target} = {target} & {name};"
            : $"{indent}    if ({name} is not null) {target} = {target} is null ? {name} : ({target} {(parentOr.Value ? "|" : "&")} {name});");
        AppendLine($"{indent}}}");
    }

    /// <summary>The sorts as an ordering of the engine's keys, the entity's key last so the order is total.</summary>
    private void RenderOrdering(QueryOrderingModel ordering)
    {
        AppendLine($"    internal {ordering.Ordering} {ordering.Method}()");
        AppendLine("    {");
        AppendLine($"        var ordering = {ordering.Ordering}.By(__OrderBy_PersistenceId);");
        foreach (var sort in ordering.Sorts)
        {
            var fallback = sort.Direction < 0 ? "null" : $"(global::Pragmatic.Persistence.Query.SortDirection)({sort.Direction})";
            AppendLine($"        if ((this.{sort.Name} ?? {fallback}) is {{ }} {sort.Name}Direction)");
            AppendLine($"            ordering = ordering.Then(__OrderBy_{sort.Key}, {sort.Name}Direction == global::Pragmatic.Persistence.Query.SortDirection.Descending);");
        }

        AppendLine("        return ordering;");
        AppendLine("    }");
        foreach (var key in ordering.Keys)
            AppendLine($"    private static readonly {ordering.Key} __OrderBy_{key.Key} = new({string.Join(", ", key.Texts.Select(SqlInterceptorGenerator.Literal))});");
    }
}
