using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     <c>CompiledPredicates</c>, the specifications' predicates compiled where they are written, and <c>Interceptors</c>: one method per call
///     site the translation took, in the order of the call sites, the specifications' last.
/// </summary>
internal sealed partial class SqlSourceTemplate
{
    private void RenderCompiledPredicates(EquatableArray<CompiledPredicateModel> predicates)
    {
        if (predicates.Count == 0)
            return;
        AppendLine("    /// <summary>The predicates of the specifications written in this compilation, compiled where they are written.</summary>");
        AppendLine("    file static class CompiledPredicates");
        AppendLine("    {");
        foreach (var p in predicates)
            AppendLine($"        internal static readonly {Sql}.Planning.SqlCompiledPredicate {p.Field} = new({Literal(p.Sqlite)}, {Literal(p.PostgreSql)}, {Literal(p.SqlServer)}, {Literal(p.MySql)}, {Literal(p.Oracle)}, new {Sql}.Planning.SqlParamKind[] {{ {Join(p.Kinds.Select(k => $"{Sql}.Planning.SqlParamKind.{k}"))} }});");
        AppendLine("    }");
        AppendLine();
    }

    private void RenderInterceptors(EquatableArray<InterceptorModel> interceptors)
    {
        AppendLine("    file static class Interceptors");
        AppendLine("    {");
        foreach (var interceptor in interceptors)
        {
            if (interceptor.Comment is not null)
                AppendLine($"        // {interceptor.Comment}");
            AppendLine($"        {interceptor.Attribute}");
            var signature = $"        internal static {interceptor.ReturnType} {interceptor.Name}{interceptor.TypeParameters}({Join(interceptor.Parameters)}){interceptor.Constraint}";
            if (interceptor.BodyOnNextLine)
            {
                AppendLine(signature);
                AppendLine($"            => {interceptor.Body};");
            }
            else
            {
                AppendLine($"{signature} => {interceptor.Body};");
            }

            AppendLine();
        }

        AppendLine("    }");
    }
}
