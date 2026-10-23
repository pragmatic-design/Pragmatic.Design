using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A hierarchy with a table per type (<c>[Inheritance(Strategy = InheritanceStrategy.Tpt)]</c>): the root's [Table] holds the root's columns
///     of every row, each derived type's [Table] its key and its own columns. The reads go through a view, <c>{RootTable}View</c>: the root's
///     table LEFT JOIN each derived table on the key, the discriminator the name of the type whose table holds the row (the root's when none
///     does). As for TPC, the generator does not run DDL: it emits the view's text as <c>{Root}View.CreateSql(dialect)</c>.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private static void EmitTptViews(List<TrailerSectionModel> extensions,IEnumerable<EntityModel> entities, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
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
            extensions.Add(new InheritanceViewModel(NamespaceOf(root.Type), root.Type.Name, root.Table, Tpt: true, root.WriteTable, parts.Select(p => p.Model.PartTable!).ToEquatableArray(), select, PlanBuilder.ForDialectText(select, SqlDialect.SqlServer)));
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
