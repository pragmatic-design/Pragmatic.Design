using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A hierarchy with a table per type (<c>[Inheritance(Strategy = InheritanceStrategy.Tpt)]</c>): the root's [Table] holds the root's columns
///     of every row, each derived type's [Table] its key and its own columns. The reads go through a view, <c>{RootTable}View</c>: the root's
///     table LEFT JOIN each derived table on the key, the discriminator the name of the type whose table holds the row (the root's when none
///     does). As for TPC, the generator does not run DDL: it emits the view's text as <c>{Root}View.CreateSql(dialect)</c>.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    private static void EmitTptViews(StringBuilder extensions, IEnumerable<EntityModel> entities, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        foreach (var root in entities.Where(e => e.Tpt && e.IsHierarchyRoot))
        {
            var where = root.Type.Locations.FirstOrDefault() ?? Location.None;
            // Every level's table (a type at any depth: a sports car's row is in Vehicles, Cars and SportsCars), the deepest first for the discriminator.
            var parts = new List<(DerivedModel Derived, EntityModel Model)>();
            foreach (var derived in root.Derived!.OrderByDescending(d => LevelsBelow(d.Type, root.Type)).ThenBy(d => d.Name, StringComparer.Ordinal))
            {
                if (lookup(derived.Type) is { PartTable: not null } model)
                    parts.Add((derived, model));
            }

            if (root.Identity || root.HasCompositeKey)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InheritanceShape, where, root.Type.Name, "a table-per-type hierarchy has a single-column key the application assigns: the derived table's row takes the same key, in the same command"));
                continue;
            }

            var key = root.KeyColumn;
            var discriminator = root.Columns.First(c => c.IsDiscriminator).Column;
            var cases = string.Join(" ", parts.Select((p, i) => $"WHEN d{i}.\"{key}\" IS NOT NULL THEN '{p.Derived.Name}'"));
            var columns = root.Columns.Where(c => !c.IsDiscriminator && c.DerivedOf is null).Select(c => $"r.\"{c.Column}\"")
                .Append($"CASE {cases} ELSE '{root.Type.Name}' END AS \"{discriminator}\"")
                .Concat(parts.SelectMany((p, i) => p.Derived.Columns.Where(c => c.DerivedOf == p.Derived.FullName).Select(c => $"d{i}.\"{c.Column}\""))); // each table its own type's columns
            var select = $"SELECT {string.Join(", ", columns)} FROM \"{root.WriteTable}\" r" + string.Concat(parts.Select((p, i) => $" LEFT JOIN \"{p.Model.PartTable}\" d{i} ON d{i}.\"{key}\" = r.\"{key}\""));
            var view = root.Table;
            var ns = root.Type.ContainingNamespace.IsGlobalNamespace ? null : root.Type.ContainingNamespace.ToDisplayString();
            const string dialect = "global::Pragmatic.Persistence.Sql.SqlDialect";
            extensions.AppendLine();
            if (ns is not null)
                extensions.AppendLine($"namespace {ns}");
            if (ns is not null)
                extensions.AppendLine("{");
            extensions.AppendLine($"    /// <summary>The view \"{view}\" over \"{root.WriteTable}\" and the derived types' tables ({string.Join(", ", parts.Select(p => $"\"{p.Model.PartTable}\""))}), generated from [Inheritance(Strategy = Tpt)]: its text per dialect, for the application's schema.</summary>");
            extensions.AppendLine($"    public static class {root.Type.Name}View");
            extensions.AppendLine("    {");
            extensions.AppendLine($"        public const string Name = {Literal(view)};");
            extensions.AppendLine();
            extensions.AppendLine("        /// <summary>The view's body: the root's table LEFT JOIN each derived table, the discriminator from the one that holds the row.</summary>");
            extensions.AppendLine($"        public static string SelectSql({dialect} dialect) => dialect == {dialect}.SqlServer ? {Literal(PlanBuilder.ForDialectText(select, SqlDialect.SqlServer))} : {Literal(select)};");
            extensions.AppendLine();
            extensions.AppendLine("        /// <summary>CREATE VIEW (CREATE OR REPLACE on PostgreSQL, MySQL and Oracle, CREATE OR ALTER on SQL Server; SQLite has neither: DropSql first).</summary>");
            extensions.AppendLine($"        public static string CreateSql({dialect} dialect) => dialect switch");
            extensions.AppendLine("        {");
            extensions.AppendLine($"            {dialect}.PostgreSql or {dialect}.MySql or {dialect}.Oracle => \"CREATE OR REPLACE VIEW \\\"{view}\\\" AS \" + SelectSql(dialect),");
            extensions.AppendLine($"            {dialect}.SqlServer => \"CREATE OR ALTER VIEW [{view}] AS \" + SelectSql(dialect),");
            extensions.AppendLine($"            _ => \"CREATE VIEW \\\"{view}\\\" AS \" + SelectSql(dialect),");
            extensions.AppendLine("        };");
            extensions.AppendLine();
            extensions.AppendLine("        /// <summary>DROP VIEW when it exists.</summary>");
            extensions.AppendLine($"        public static string DropSql({dialect} dialect) => dialect == {dialect}.SqlServer ? \"IF OBJECT_ID('{view}', 'V') IS NOT NULL DROP VIEW [{view}]\" : \"DROP VIEW IF EXISTS \\\"{view}\\\"\";");
            extensions.AppendLine("    }");
            if (ns is not null)
                extensions.AppendLine("}");
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, $"[Inheritance(Strategy = Tpt)] {root.Type.Name}", select));
        }
    }

    private static int LevelsBelow(INamedTypeSymbol type, INamedTypeSymbol root)
    {
        var levels = 0;
        for (var current = type; current is not null && !SymbolEqualityComparer.Default.Equals(current, root); current = current.BaseType)
            levels++;
        return levels;
    }
}
