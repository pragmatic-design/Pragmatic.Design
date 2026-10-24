using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     <c>Pragmatic.Persistence.Sql.Queries.g.cs</c>: the <c>[Query]</c> definitions as session extensions, and the implementing parts of the
///     <c>[SqlFunction]</c>s. The generated methods repeat the definitions' expressions, so the file carries the definitions' usings.
/// </summary>
internal sealed class SqlQueriesTemplate(IEnumerable<string> usings, EquatableArray<DeclaredSectionModel> sections) : CSharpTemplate
{
    private const string Sql = "global::Pragmatic.Persistence.Sql";

    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => "[Query] definitions as session extensions";

    public override Artifact RenderOutput() => new(VirtualFolderHints.ForAssembly("Sql", "Queries"), ToSourceText());

    public override void RenderFile()
    {
        foreach (var directive in usings)
            AppendLine(directive);
        foreach (var section in sections)
        {
            AppendLine();
            if (section.Namespace is not null)
            {
                AppendLine($"namespace {section.Namespace}");
                AppendLine("{");
            }

            AppendLine($"    static partial class {section.ClassName}");
            AppendLine("    {");
            if (section is DeclaredQueryModel query)
                RenderQuery(query);
            else
                RenderFunctionBodies((FunctionBodiesModel)section);
            AppendLine("    }");
            if (section.Namespace is not null)
                AppendLine("}");
        }
    }

    private void RenderQuery(DeclaredQueryModel q)
    {
        var parameters = string.Concat(q.Parameters.Select(p => ", " + p));
        AppendLine($"        /// <summary>Generated from the [Query] definition <c>{Escape(q.MethodName)}({Escape(string.Join(", ", q.DefinitionParameterTypes))})</c>: <c>{Escape(q.PostgreSql)}</c></summary>");
        AppendLine($"        public static {q.ReturnType} {q.MethodName}(this {Sql}.SqlSession session{parameters}, global::System.Threading.CancellationToken ct = default)");
        AppendLine($"            => {q.Call};");
        if (q.BatchResultType is null)
            return;
        AppendLine();
        AppendLine("        /// <summary>The same query in a batch: recorded now, run by <c>batch.ExecuteAsync()</c>, answered by the item's <c>Result</c>.</summary>");
        AppendLine($"        public static {Sql}.Writing.SqlBatchItem<{q.BatchResultType}> {q.MethodName}(this {Sql}.Writing.SqlBatch batch{parameters})");
        AppendLine($"            => {q.BatchBody};");
    }

    private void RenderFunctionBodies(FunctionBodiesModel functions)
    {
        foreach (var f in functions.Functions)
            AppendLine($"        {f.Access} static partial {f.ReturnType} {f.Name}({string.Join(", ", f.Parameters)}) => throw new global::System.NotSupportedException(\"{f.Name} is a [SqlFunction]: it is translated to SQL inside a query's lambda and does not run in .NET.\");");
    }

    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
