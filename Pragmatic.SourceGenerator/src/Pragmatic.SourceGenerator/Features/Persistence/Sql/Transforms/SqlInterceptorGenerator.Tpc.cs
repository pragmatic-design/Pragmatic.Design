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
///     A hierarchy with a table per concrete type (<c>[Inheritance(Strategy = InheritanceStrategy.Tpc)]</c>): the root's <c>[Table]</c>
///     names a view, <c>UNION ALL</c> of the derived types' tables in the root's column order, the type's name as the discriminator and
///     NULL for the other types' columns. The generator does not run DDL; it emits the view's text as <c>{Root}View.CreateSql(dialect)</c>
///     (with <c>DropSql</c> and <c>SelectSql</c>) in the root's namespace, for the application's migrations.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private static void EmitTpcViews(List<TrailerSectionModel> extensions,IEnumerable<EntityModel> entities, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
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
            extensions.Add(new InheritanceViewModel(NamespaceOf(root.Type), root.Type.Name, root.Table, Tpt: false, WriteTable: null, tables.Select(t => t.Model.Table).ToEquatableArray(), select, PlanBuilder.ForDialectText(select, SqlDialect.SqlServer)));
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
