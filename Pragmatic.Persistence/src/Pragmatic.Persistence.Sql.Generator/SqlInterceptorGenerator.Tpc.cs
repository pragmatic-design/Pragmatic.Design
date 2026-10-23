using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A hierarchy with a table per concrete type (<c>[Inheritance(Strategy = InheritanceStrategy.Tpc)]</c>): the root's <c>[Table]</c>
///     names a view, <c>UNION ALL</c> of the derived types' tables in the root's column order, the type's name as the discriminator and
///     NULL for the other types' columns. The generator does not run DDL; it emits the view's text as <c>{Root}View.CreateSql(dialect)</c>
///     (with <c>DropSql</c> and <c>SelectSql</c>) in the root's namespace, for the application's migrations.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    private static void EmitTpcViews(StringBuilder extensions, IEnumerable<EntityModel> entities, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        foreach (var root in entities.Where(e => e.Tpc && e.IsHierarchyRoot))
        {
            var where = root.Type.Locations.FirstOrDefault() ?? Location.None;
            var tables = new List<(DerivedModel Derived, EntityModel Model)>();
            foreach (var derived in root.Derived!)
            {
                var model = lookup(derived.Type);
                if (model is null)
                    continue; // reported where the derived type was read
                if (model.Table == root.Table)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InheritanceShape, where, root.Type.Name, $"{derived.Name} has the root's table \"{root.Table}\": in a table-per-concrete-type hierarchy every derived type names its own [Table], the root's is the view over them"));
                    return;
                }

                tables.Add((derived, model));
            }

            if (tables.Count == 0)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InheritanceShape, where, root.Type.Name, "a table-per-concrete-type hierarchy has at least one [Entity] derived type, each with its own table"));
                continue;
            }

            var select = TpcSelect(root, tables);
            var ns = root.Type.ContainingNamespace.IsGlobalNamespace ? null : root.Type.ContainingNamespace.ToDisplayString();
            const string dialect = "global::Pragmatic.Persistence.Sql.SqlDialect";
            extensions.AppendLine();
            if (ns is not null)
                extensions.AppendLine($"namespace {ns}");
            if (ns is not null)
                extensions.AppendLine("{");
            extensions.AppendLine($"    /// <summary>The view \"{root.Table}\" over the tables of <see cref=\"{root.Type.Name}\" />'s derived types ({string.Join(", ", tables.Select(t => $"\"{t.Model.Table}\""))}), generated from [Inheritance(Strategy = Tpc)]: its text per dialect, for the application's schema.</summary>");
            extensions.AppendLine($"    public static class {root.Type.Name}View");
            extensions.AppendLine("    {");
            extensions.AppendLine($"        public const string Name = {Literal(root.Table)};");
            extensions.AppendLine();
            extensions.AppendLine("        /// <summary>The view's body: one SELECT per derived type, UNION ALL, the type's name as the discriminator.</summary>");
            extensions.AppendLine($"        public static string SelectSql({dialect} dialect) => dialect == {dialect}.SqlServer ? {Literal(PlanBuilder.ForDialectText(select, SqlDialect.SqlServer))} : {Literal(select)};");
            extensions.AppendLine();
            extensions.AppendLine("        /// <summary>CREATE VIEW (CREATE OR REPLACE on PostgreSQL and MySQL, CREATE OR ALTER on SQL Server; SQLite has neither: DropSql first).</summary>");
            extensions.AppendLine($"        public static string CreateSql({dialect} dialect) => dialect switch");
            extensions.AppendLine("        {");
            extensions.AppendLine($"            {dialect}.PostgreSql or {dialect}.MySql or {dialect}.Oracle => \"CREATE OR REPLACE VIEW \\\"{root.Table}\\\" AS \" + SelectSql(dialect),");
            extensions.AppendLine($"            {dialect}.SqlServer => \"CREATE OR ALTER VIEW [{root.Table}] AS \" + SelectSql(dialect),");
            extensions.AppendLine($"            _ => \"CREATE VIEW \\\"{root.Table}\\\" AS \" + SelectSql(dialect),");
            extensions.AppendLine("        };");
            extensions.AppendLine();
            extensions.AppendLine("        /// <summary>DROP VIEW when it exists.</summary>");
            extensions.AppendLine($"        public static string DropSql({dialect} dialect) => dialect == {dialect}.SqlServer ? \"IF OBJECT_ID('{root.Table}', 'V') IS NOT NULL DROP VIEW [{root.Table}]\" : \"DROP VIEW IF EXISTS \\\"{root.Table}\\\"\";");
            extensions.AppendLine("    }");
            if (ns is not null)
                extensions.AppendLine("}");
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, $"[Inheritance(Strategy = Tpc)] {root.Type.Name}", select));
        }
    }

    /// <summary>The root's columns from each derived type's table: its own and the inherited ones as they are, the discriminator as the type's name, the other types' columns NULL.</summary>
    private static string TpcSelect(EntityModel root, List<(DerivedModel Derived, EntityModel Model)> tables)
    {
        var branches = new List<string>();
        foreach (var (derived, model) in tables)
        {
            var columns = root.Columns.Select(c =>
                c.IsDiscriminator ? $"'{derived.Name}' AS \"{c.Column}\""
                : c.DerivedOf is null || c.DerivedOf == derived.FullName ? $"t.\"{c.Column}\""
                : $"NULL AS \"{c.Column}\"");
            branches.Add($"SELECT {string.Join(", ", columns)} FROM \"{model.Table}\" t");
        }

        return string.Join(" UNION ALL ", branches);
    }
}
