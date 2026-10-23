using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Models;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     <c>[GenerateHierarchy]</c>: for an entity with a navigation to itself, two plans with a recursive CTE (the descendants of a
///     key, its ancestors; the node itself excluded) and the two session extensions that run them, named as the repository's
///     (<c>GetDescendantsByParent</c>, <c>GetAncestorsByParent</c>). The CTE walks the foreign key; the rows come out through the
///     entity's reader. ASSUMPTION: the entity's visibility rules are not applied inside the walk.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    /// <summary>The plans into <paramref name="plans" /> (inside the generated namespace) and the extension classes into <paramref name="extensions" /> (in the entity's namespace, appended after it).</summary>
    private static void EmitHierarchies(List<PlanSectionModel> plans, StringBuilder extensions, IEnumerable<EntityModel> entities, List<Diagnostic> diagnostics)
    {
        foreach (var entity in entities)
        {
            foreach (var attribute in entity.Type.GetAttributes().Where(a => a.AttributeClass?.Name == "GenerateHierarchyAttribute"))
            {
                var via = attribute.NamedArguments.FirstOrDefault(n => n.Key == "Via").Value.Value as string;
                var self = entity.Navigations.Where(n => SymbolEqualityComparer.Default.Equals(n.Target, entity.Type)).ToList();
                var navigation = via is null ? (self.Count == 1 ? self[0] : null) : self.FirstOrDefault(n => n.Property == via);
                var where = entity.Type.Locations.FirstOrDefault() ?? Location.None;
                if (navigation is null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.HierarchyShape, where, entity.Type.Name, via is null
                        ? (self.Count == 0 ? "it has no navigation to itself" : $"it has {self.Count} navigations to itself ({string.Join(", ", self.Select(n => n.Property))}): say which with Via")
                        : $"'{via}' is not a navigation of {entity.Type.Name} to itself"));
                    continue;
                }

                if (entity.HasCompositeKey)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.CompositeKey, where, entity.Type.Name, entity.KeyList, "a hierarchy walks a single-column key"));
                    continue;
                }

                var key = entity.Keys[0];
                var foreignKey = entity.Column(navigation.ForeignKey)?.Column ?? navigation.ForeignKey;
                var descendants = $"Hierarchy_{Safe(entity)}_{navigation.Property}_Descendants";
                var ancestors = $"Hierarchy_{Safe(entity)}_{navigation.Property}_Ancestors";
                plans.Add(HierarchyPlan(descendants, entity, key, $"t.\"{foreignKey}\" = @p0", $"t.\"{foreignKey}\" = tree.\"{key.Column}\""));
                plans.Add(HierarchyPlan(ancestors, entity, key, $"t.\"{key.Column}\" = @p0", $"t.\"{key.Column}\" = tree.\"{foreignKey}\""));

                var keyType = key.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var list = $"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.List<{entity.FullName}>>";
                var ns = entity.Type.ContainingNamespace.IsGlobalNamespace ? null : entity.Type.ContainingNamespace.ToDisplayString();
                extensions.AppendLine();
                if (ns is not null)
                    extensions.AppendLine($"namespace {ns}");
                if (ns is not null)
                    extensions.AppendLine("{");
                extensions.AppendLine($"    /// <summary>The tree of <see cref=\"{entity.Type.Name}\" /> along <c>{navigation.Property}</c>, generated from [GenerateHierarchy]: one recursive CTE per call, the node itself excluded.</summary>");
                extensions.AppendLine($"    public static class {entity.Type.Name}{navigation.Property}Hierarchy");
                extensions.AppendLine("    {");
                extensions.AppendLine($"        public static {list} GetDescendantsBy{navigation.Property}(this global::Pragmatic.Persistence.Sql.SqlSession session, {keyType} key, global::System.Threading.CancellationToken ct = default)");
                extensions.AppendLine($"            => global::Pragmatic.Persistence.Sql.Generated.{descendants}.Instance.ToListAsync(session.Query<{entity.FullName}>().WithSlot(0, key), ct);");
                extensions.AppendLine();
                extensions.AppendLine($"        public static {list} GetAncestorsBy{navigation.Property}(this global::Pragmatic.Persistence.Sql.SqlSession session, {keyType} key, global::System.Threading.CancellationToken ct = default)");
                extensions.AppendLine($"            => global::Pragmatic.Persistence.Sql.Generated.{ancestors}.Instance.ToListAsync(session.Query<{entity.FullName}>().WithSlot(0, key), ct);");
                extensions.AppendLine("    }");
                if (ns is not null)
                    extensions.AppendLine("}");
                diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, $"[GenerateHierarchy] {entity.Type.Name}.{navigation.Property}", HierarchySql(entity, SqlDialect.Sqlite, $"t.\"{foreignKey}\" = @p0", $"t.\"{foreignKey}\" = tree.\"{key.Column}\"", key)));
            }
        }
    }

    /// <summary>A plan running the walk: the key as its one parameter, the entity's reader, no loader.</summary>
    private static HierarchyPlanModel HierarchyPlan(string className, EntityModel entity, ColumnModel key, string anchor, string step)
        => new(
            className,
            entity.FullName,
            HierarchySql(entity, SqlDialect.Sqlite, anchor, step, key),
            HierarchySql(entity, SqlDialect.PostgreSql, anchor, step, key),
            HierarchySql(entity, SqlDialect.SqlServer, anchor, step, key),
            HierarchySql(entity, SqlDialect.MySql, anchor, step, key),
            HierarchySql(entity, SqlDialect.Oracle, anchor, step, key),
            LambdaTranslator.KindOf(key.Store),
            Safe(entity),
            $"[GenerateHierarchy] {entity.Type.Name} {className}");

    /// <summary>
    ///     <c>WITH [RECURSIVE] tree AS (anchor UNION ALL step) SELECT … FROM tree t WHERE t.key &lt;&gt; @p0</c>: SQLite and PostgreSQL say
    ///     RECURSIVE, SQL Server does not; identifiers quoted as the fragments are, then turned for the dialect.
    /// </summary>
    private static string HierarchySql(EntityModel entity, SqlDialect dialect, string anchor, string step, ColumnModel key)
    {
        var columns = string.Join(", ", entity.Columns.Select(c => c.Select("t")));
        var sql = new StringBuilder();
        sql.Append(dialect switch
        {
            SqlDialect.SqlServer => "WITH tree AS (",
            SqlDialect.Oracle => "WITH tree (" + string.Join(", ", entity.Columns.Select(c => "\"" + c.Column + "\"")) + ") AS (", // Oracle names the recursive WITH's columns, no RECURSIVE
            _ => "WITH RECURSIVE tree AS (",
        });
        // The same text session.With(anchor, step).Where(…) builds (the predicates in parentheses), the key bound once.
        sql.Append("SELECT ").Append(columns).Append(" FROM \"").Append(entity.Table).Append("\" t WHERE (").Append(anchor).Append(')');
        sql.Append(" UNION ALL SELECT ").Append(columns).Append(" FROM \"").Append(entity.Table).Append("\" t INNER JOIN tree ON (").Append(step).Append(')');
        sql.Append(") SELECT ").Append(columns).Append(" FROM tree t WHERE (t.\"").Append(key.Column).Append("\" <> @p0)");
        return PlanBuilder.ForDialectText(sql.ToString(), dialect);
    }
}
