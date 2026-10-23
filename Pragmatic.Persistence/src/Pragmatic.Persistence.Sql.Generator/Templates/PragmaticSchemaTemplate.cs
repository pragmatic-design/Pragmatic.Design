using System.Linq;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

/// <summary>
///     The schema of Pragmatic's <c>[Entity]</c> types the Sql engine serves, for Pragmatic.Migrations, in the shape Pragmatic's EF branch gives it
///     (<c>SchemaMetadataTemplate</c>): <c>DefaultSchema.Current</c> on the referenced driver's provider, <c>For(SqlDialect)</c> on each. Its own
///     source, needing Pragmatic.Migrations and the engine's <c>SqlDialect</c> only, so it compiles alone.
/// </summary>
internal sealed class PragmaticSchemaTemplate(PragmaticSchemaModel model) : CSharpTemplate
{
    private const string Dialect = "global::Pragmatic.Persistence.Sql.SqlDialect";
    private const string SchemaVersion = "global::Pragmatic.Migrations.Schema.SchemaVersion";

    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => "the schema of Pragmatic's entities the Sql engine serves, for Pragmatic.Migrations";

    public override Artifact RenderOutput() => new("Pragmatic.Persistence.Sql.PragmaticSchema.g.cs", ToSourceText());

    public override void RenderFile()
    {
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"/// <summary>The schema of database Default ({model.EntityCount} entities), for Pragmatic Migrations' diff engine: <c>Current</c> on the provider of the referenced driver, <c>For</c> on each.</summary>");
        AppendLine("public static class DefaultSchema");
        AppendLine("{");
        if (model.Current is not null)
            AppendLine($"    public static readonly {SchemaVersion} Current = For({Dialect}.{model.Current});");
        AppendLine();
        AppendLine($"    public static {SchemaVersion} For({Dialect} dialect) => dialect switch");
        AppendLine("    {");
        foreach (var d in model.Dialects)
        {
            AppendLine($"        {Dialect}.{d.Dialect} => new([");
            var tables = d.Tables.Select(MigrationSchemaText.Table).ToList();
            for (var i = 0; i < tables.Count; i++)
                AppendLine($"            {tables[i]}{(i < tables.Count - 1 ? "," : "")}");
            AppendLine($"        ], \"Default\", {SqlInterceptorGenerator.Literal(d.Provider)}, null),");
        }

        AppendLine("        _ => throw new global::System.NotSupportedException($\"The spike describes Pragmatic's schema on PostgreSQL, SQL Server, SQLite, MySQL and Oracle, not {dialect}.\"),");
        AppendLine("    };");
        AppendLine("}");
    }
}
