using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     <c>PageAsync</c> on the Sql engine (#946): the specification through <c>Matching</c>, the ordering's members mapped to the keys the
///     generator compiled for them, the page taken and the matches counted, each a chain the generator binds and intercepts.
/// </summary>
internal sealed partial class ReadRepositoryTemplate
{
    private void RenderPageAsync()
    {
        var type = model.EntityType;
        var ordering = $"global::Pragmatic.Persistence.Sql.Planning.SqlOrdering<{type}>";
        var filtered = $"session.Query<{type}>().{Matching}";
        AppendLine("        /// <summary>A page of the rows the specification matches, in the ordering, with the total: a member the ordering names that has no compiled key is refused.</summary>");
        AppendLine($"        public async {Task}<global::Pragmatic.Persistence.Query.Results.PagedResult<{type}>> PageAsync({Spec}<{type}> spec, global::Pragmatic.Persistence.Query.Ordering<{type}> ordering, int page, int pageSize, {Token})");
        AppendLine("        {");
        AppendLine("            global::Pragmatic.Ensure.Ensure.ThrowIfNull(ordering);");
        AppendLine($"            var sorted = {ordering}.By(__PageBy_PersistenceId);");
        AppendLine("            foreach (var key in ordering.Keys)");
        AppendLine("            {");
        // Typed: with no grid filter of the entity the switch has the refusal alone, which has no type.
        AppendLine("                global::Pragmatic.Persistence.Sql.Planning.SqlOrderKey compiled = key.Member switch");
        AppendLine("                {");
        foreach (var key in model.PageKeys.Where(k => k.Member != "PersistenceId"))
            AppendLine($"                    {SqlInterceptorGenerator.Literal(key.Member)} => __PageBy_{key.Key},");
        AppendLine($"                    _ => throw new global::System.NotSupportedException($\"The Sql engine compiled no sort of {model.EntityName} on '{{key.Member}}': a key is compiled for each member a [GridFilter] of {model.EntityName} sorts by.\"),");
        AppendLine("                };");
        AppendLine("                sorted = sorted.Then(compiled, key.Descending);");
        AppendLine("            }");
        AppendLine();
        AppendLine("            var rows = await " + filtered + ".OrderedBy(sorted).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);");
        AppendLine("            var total = await " + filtered + ".CountAsync(ct);");
        AppendLine($"            return global::Pragmatic.Persistence.Query.Results.PagedResult<{type}>.Success(rows, total, page, pageSize);");
        AppendLine("        }");
        AppendLine();
        foreach (var key in model.PageKeys)
            AppendLine($"        private static readonly global::Pragmatic.Persistence.Sql.Planning.SqlOrderKey __PageBy_{key.Key} = new({string.Join(", ", key.Texts.Select(SqlInterceptorGenerator.Literal))});");
        AppendLine();
    }
}
