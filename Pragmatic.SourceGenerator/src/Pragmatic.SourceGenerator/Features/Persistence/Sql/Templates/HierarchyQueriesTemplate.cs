using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     Pragmatic's <c>[GenerateHierarchy]</c> on an engine: per tree, <c>GetDescendantsBy{Navigation}Async</c> and
///     <c>GetAncestorsBy{Navigation}Async</c>. The engine walks the parent keys; the rows are then read with a chain on the session, so the
///     caller's filters apply to them.
/// </summary>
internal sealed class HierarchyQueriesTemplate(HierarchyQueriesModel model) : CSharpTemplate
{
    protected override string? GeneratorName => model.Package;

    protected override string? SourceInfo => $"the hierarchy queries of {model.EntityName} ([GenerateHierarchy])";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var type = model.EntityType;
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.EntityName}");
        AppendLine("{");
        foreach (var tree in model.Trees)
        {
            foreach (var (name, descendants, start) in new[] { ("GetDescendantsBy" + tree.Navigation, true, "rootId"), ("GetAncestorsBy" + tree.Navigation, false, "childId") })
            {
                AppendLine($"    /// <summary>The {(descendants ? "descendants" : "ancestors")} of the {model.EntityName} along {tree.Navigation} (the row itself left out), within the session's filters.</summary>");
                AppendLine($"    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{type}>> {name}Async({model.Session} session, global::System.Guid {start}, global::System.Threading.CancellationToken ct = default)");
                AppendLine("    {");
                AppendLine($"        var keys = await {model.Keys}<{type}>(session, {PragmaticNames.Literal(tree.ForeignKey)}, {start}, descendants: {(descendants ? "true" : "false")}, ct: ct);");
                AppendLine($"        return await session.Query<{type}>().Where((e, k) => k.Contains(e.PersistenceId), keys).ToListAsync(ct);");
                AppendLine("    }");
                AppendLine();
            }
        }

        AppendLine("}");
    }
}
