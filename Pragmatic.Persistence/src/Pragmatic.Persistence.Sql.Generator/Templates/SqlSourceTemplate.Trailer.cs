using System.Linq;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

/// <summary>
///     What follows the generated namespace, each in its own namespace: the hierarchy walks' session extensions, the views of the inheritance
///     hierarchies read through one, and the <c>[SqlDatabase]</c> classes' schemas for Pragmatic.Migrations. The generator runs no DDL: the
///     views and the schemas are text for the application's migrations.
/// </summary>
internal sealed partial class SqlSourceTemplate
{
    private const string Dialect = "global::Pragmatic.Persistence.Sql.SqlDialect";
    private const string Migrations = "global::Pragmatic.Migrations.Schema.";

    private void RenderTrailer(EquatableArray<TrailerSectionModel> sections)
    {
        foreach (var section in sections)
        {
            AppendLine();
            if (section.Namespace is not null)
            {
                AppendLine($"namespace {section.Namespace}");
                AppendLine("{");
            }

            switch (section)
            {
                case HierarchyExtensionModel hierarchy:
                    RenderHierarchyExtensions(hierarchy);
                    break;
                case InheritanceViewModel view:
                    RenderInheritanceView(view);
                    break;
                case DatabaseSchemaModel schema:
                    RenderSchema(schema);
                    break;
            }

            if (section.Namespace is not null)
                AppendLine("}");
        }
    }

    private void RenderHierarchyExtensions(HierarchyExtensionModel h)
    {
        var list = $"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.List<{h.EntityType}>>";
        AppendLine($"    /// <summary>The tree of <see cref=\"{h.EntityName}\" /> along <c>{h.Navigation}</c>, generated from [GenerateHierarchy]: one recursive CTE per call, the node itself excluded.</summary>");
        AppendLine($"    public static class {h.EntityName}{h.Navigation}Hierarchy");
        AppendLine("    {");
        AppendLine($"        public static {list} GetDescendantsBy{h.Navigation}(this {Sql}.SqlSession session, {h.KeyType} key, global::System.Threading.CancellationToken ct = default)");
        AppendLine($"            => {Sql}.Generated.{h.DescendantsPlan}.Instance.ToListAsync(session.Query<{h.EntityType}>().WithSlot(0, key), ct);");
        AppendLine();
        AppendLine($"        public static {list} GetAncestorsBy{h.Navigation}(this {Sql}.SqlSession session, {h.KeyType} key, global::System.Threading.CancellationToken ct = default)");
        AppendLine($"            => {Sql}.Generated.{h.AncestorsPlan}.Instance.ToListAsync(session.Query<{h.EntityType}>().WithSlot(0, key), ct);");
        AppendLine("    }");
    }

    private void RenderInheritanceView(InheritanceViewModel v)
    {
        var tables = string.Join(", ", v.Tables.Select(t => $"\"{t}\""));
        AppendLine(v.Tpt
            ? $"    /// <summary>The view \"{v.View}\" over \"{v.WriteTable}\" and the derived types' tables ({tables}), generated from [Inheritance(Strategy = Tpt)]: its text per dialect, for the application's schema.</summary>"
            : $"    /// <summary>The view \"{v.View}\" over the tables of <see cref=\"{v.RootName}\" />'s derived types ({tables}), generated from [Inheritance(Strategy = Tpc)]: its text per dialect, for the application's schema.</summary>");
        AppendLine($"    public static class {v.RootName}View");
        AppendLine("    {");
        AppendLine($"        public const string Name = {Literal(v.View)};");
        AppendLine();
        AppendLine(v.Tpt
            ? "        /// <summary>The view's body: the root's table LEFT JOIN each derived table, the discriminator from the one that holds the row.</summary>"
            : "        /// <summary>The view's body: one SELECT per derived type, UNION ALL, the type's name as the discriminator.</summary>");
        AppendLine($"        public static string SelectSql({Dialect} dialect) => dialect == {Dialect}.SqlServer ? {Literal(v.SelectSqlServer)} : {Literal(v.Select)};");
        AppendLine();
        AppendLine(v.Tpt
            ? "        /// <summary>CREATE VIEW (CREATE OR REPLACE on PostgreSQL, MySQL and Oracle, CREATE OR ALTER on SQL Server; SQLite has neither: DropSql first).</summary>"
            : "        /// <summary>CREATE VIEW (CREATE OR REPLACE on PostgreSQL and MySQL, CREATE OR ALTER on SQL Server; SQLite has neither: DropSql first).</summary>");
        AppendLine($"        public static string CreateSql({Dialect} dialect) => dialect switch");
        AppendLine("        {");
        AppendLine($"            {Dialect}.PostgreSql or {Dialect}.MySql or {Dialect}.Oracle => \"CREATE OR REPLACE VIEW \\\"{v.View}\\\" AS \" + SelectSql(dialect),");
        AppendLine($"            {Dialect}.SqlServer => \"CREATE OR ALTER VIEW [{v.View}] AS \" + SelectSql(dialect),");
        AppendLine($"            _ => \"CREATE VIEW \\\"{v.View}\\\" AS \" + SelectSql(dialect),");
        AppendLine("        };");
        AppendLine();
        AppendLine("        /// <summary>DROP VIEW when it exists.</summary>");
        AppendLine($"        public static string DropSql({Dialect} dialect) => dialect == {Dialect}.SqlServer ? \"IF OBJECT_ID('{v.View}', 'V') IS NOT NULL DROP VIEW [{v.View}]\" : \"DROP VIEW IF EXISTS \\\"{v.View}\\\"\";");
        AppendLine("    }");
    }

    private void RenderSchema(DatabaseSchemaModel s)
    {
        AppendLine($"    partial class {s.Name}");
        AppendLine("    {");
        AppendLine($"        /// <summary>The schema of {s.Name} for Pragmatic.Migrations ({s.TableCount} tables), generated from the entities stored in it: its column types per provider.</summary>");
        AppendLine($"        public static {Migrations}SchemaVersion Schema({Dialect} dialect) => dialect switch");
        AppendLine("        {");
        foreach (var d in s.Dialects)
            AppendLine($"            {Dialect}.{d.Dialect} => new([{Join(d.Tables.Select(Table))}], {Literal(s.Name)}, global::Pragmatic.Migrations.MigrationConstants.{d.Provider}, {LiteralOrNull(s.ConfigKey)}),");
        AppendLine("            _ => throw new global::System.NotSupportedException($\"Pragmatic.Migrations migrates PostgreSQL, SQL Server and SQLite, not {dialect}.\"),");
        AppendLine("        };");
        AppendLine("    }");
    }

    private static string Table(TableSchemaModel table)
    {
        var columns = table.Columns.Select(c => $"new {Migrations}ColumnSchema({Literal(c.Name)}, {Literal(c.Type)}, {Bool(c.Nullable)}, {Bool(c.Key)}{(c.DefaultValue is null ? "" : ", " + Literal(c.DefaultValue))})");
        var indexes = table.Indexes.Select(i => $"new {Migrations}IndexSchema({Literal(i.Name)}, [{Join(i.Columns.Select(Literal))}]{(i.Unique ? ", IsUnique: true" : "")}{(i.Filter is null ? "" : $", Filter: {Literal(i.Filter)}")})");
        var foreignKeys = table.ForeignKeys.Select(f => $"new {Migrations}ForeignKeySchema({Literal(f.Name)}, {Literal(f.Column)}, {Literal(f.Table)}, {Literal(f.ReferencedColumn)}, {Migrations}ReferentialAction.NoAction)");
        return $"new {Migrations}TableSchema({Literal(table.Name)}, null, [{Join(columns)}], [{Join(indexes)}], [{Join(foreignKeys)}])";
    }
}
