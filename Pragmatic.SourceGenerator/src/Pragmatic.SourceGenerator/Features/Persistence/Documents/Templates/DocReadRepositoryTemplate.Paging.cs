using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

/// <summary>
///     <c>PageAsync</c> on the documents engine (#946), as the Sql engine's: the specification through <c>Matching</c>, the ordering's members mapped
///     to the keys the generator compiled for them, the page taken and the matches counted, each a chain the generator binds and intercepts.
/// </summary>
internal sealed partial class DocReadRepositoryTemplate
{
    private void RenderPageAsync()
    {
        var type = model.EntityType;
        var ordering = $"global::Pragmatic.Persistence.Documents.Planning.DocOrdering<{type}>";
        AppendLine("        /// <summary>A page of the documents the specification matches, in the ordering, with the total: a member the ordering names that has no compiled key is refused.</summary>");
        AppendLine($"        public async {Task}<global::Pragmatic.Persistence.Query.Results.PagedResult<{type}>> PageAsync({Spec}<{type}> spec, global::Pragmatic.Persistence.Query.Ordering<{type}> ordering, int page, int pageSize, {Token})");
        AppendLine("        {");
        AppendLine("            global::Pragmatic.Ensure.Ensure.ThrowIfNull(ordering);");
        AppendLine($"            var sorted = {ordering}.By(__PageBy_PersistenceId);");
        AppendLine("            foreach (var key in ordering.Keys)");
        AppendLine("            {");
        // Typed: with no grid filter of the entity the switch has the refusal alone, which has no type.
        AppendLine("                global::Pragmatic.Persistence.Documents.Planning.DocOrderKey compiled = key.Member switch");
        AppendLine("                {");
        foreach (var key in model.PageKeys.Where(k => k.Member != "PersistenceId"))
            AppendLine($"                    {DocTypeEmitter.Literal(key.Member)} => __PageBy_{key.Key},");
        AppendLine($"                    _ => throw new global::System.NotSupportedException($\"The documents engine compiled no sort of {model.EntityName} on '{{key.Member}}': a key is compiled for each member a [GridFilter] of {model.EntityName} sorts by.\"),");
        AppendLine("                };");
        AppendLine("                sorted = sorted.Then(compiled, key.Descending);");
        AppendLine("            }");
        AppendLine();
        AppendLine($"            var filtered = session.Query<{type}>().Matching({ToPredicate}(spec));");
        AppendLine("            var rows = await filtered.OrderedBy(sorted).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);");
        AppendLine("            var total = checked((int)await filtered.CountAsync(ct));");
        AppendLine($"            return global::Pragmatic.Persistence.Query.Results.PagedResult<{type}>.Success(rows, total, page, pageSize);");
        AppendLine("        }");
        AppendLine();
        foreach (var key in model.PageKeys)
            AppendLine($"        private static readonly global::Pragmatic.Persistence.Documents.Planning.DocOrderKey __PageBy_{key.Key} = new({string.Join(", ", key.Texts.Select(DocTypeEmitter.Literal))});");
        AppendLine();
    }
}
