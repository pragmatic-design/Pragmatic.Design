using System.Linq;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

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
            AppendLine("        return spec;");
            AppendLine("    }");
            AppendLine();
            if (model.SqlOrdering is not null)
                RenderSqlOrdering(model.SqlOrdering);
        }

        AppendLine("}");
    }

    /// <summary>The sorts as an ordering of compiled keys, the entity's key last so the order is total.</summary>
    private void RenderSqlOrdering(SqlOrderingModel ordering)
    {
        var entity = model.EntityType;
        AppendLine($"    internal global::Pragmatic.Persistence.Sql.SqlOrdering<{entity}> ToSqlOrdering()");
        AppendLine("    {");
        AppendLine($"        var ordering = global::Pragmatic.Persistence.Sql.SqlOrdering<{entity}>.By(__OrderBy_PersistenceId);");
        foreach (var sort in ordering.Sorts)
        {
            var fallback = sort.Direction < 0 ? "null" : $"(global::Pragmatic.Persistence.Query.SortDirection)({sort.Direction})";
            AppendLine($"        if ((this.{sort.Name} ?? {fallback}) is {{ }} {sort.Name}Direction)");
            AppendLine($"            ordering = ordering.Then(__OrderBy_{sort.Key}, {sort.Name}Direction == global::Pragmatic.Persistence.Query.SortDirection.Descending);");
        }

        AppendLine("        return ordering;");
        AppendLine("    }");
        foreach (var key in ordering.Keys)
            AppendLine($"    private static readonly global::Pragmatic.Persistence.Sql.SqlOrderKey __OrderBy_{key.Key} = new({string.Join(", ", key.Texts.Select(SqlInterceptorGenerator.Literal))});");
    }
}
