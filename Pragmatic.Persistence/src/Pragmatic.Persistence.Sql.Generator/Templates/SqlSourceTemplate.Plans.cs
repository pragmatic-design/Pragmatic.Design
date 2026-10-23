using System.Collections.Generic;
using System.Linq;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

/// <summary>
///     The plans: one class per translated chain, <c>[Query]</c> and hierarchy walk, each holding its <c>SqlPlan</c>, and <c>PlanRegistry</c>,
///     which registers them lazily at module start for <c>SqlSession.WarmupAsync</c>.
/// </summary>
internal sealed partial class SqlSourceTemplate
{
    private void RenderPlans(EquatableArray<PlanSectionModel> plans)
    {
        foreach (var plan in plans)
        {
            if (plan is TranslatedPlanModel translated)
                RenderPlan(translated);
            else
                RenderHierarchyPlan((HierarchyPlanModel)plan);
        }

        AppendLine("    file static class PlanRegistry");
        AppendLine("    {");
        AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("        internal static void Register()");
        AppendLine("        {");
        foreach (var plan in plans.Where(p => p.Registered))
            AppendLine($"            {Sql}.SqlPlans.Add(static () => {plan.ClassName}.Instance);");
        AppendLine("        }");
        AppendLine("    }");
        AppendLine();
    }

    private void RenderPlan(TranslatedPlanModel plan)
    {
        var kinds = plan.Kinds.Count == 0
            ? $"global::System.Array.Empty<{Sql}.SqlParamKind>()"
            : $"new {Sql}.SqlParamKind[] {{ " + Join(plan.Kinds.Select(k => $"{Sql}.SqlParamKind.{k}")) + " }";
        if (plan.Origin is not null)
            AppendLine($"    /// <summary>{plan.Origin}</summary>");
        AppendLine($"    {(plan.FileScoped ? "file" : "internal")} static class {plan.TypeName}");
        AppendLine("    {");
        foreach (var field in plan.Fields)
            AppendLine($"        {field}");
        AppendLine($"        internal static readonly SqlPlan<{plan.ElementType}> Instance = new(");
        AppendLine($"            {Literal(plan.Sqlite)},");
        AppendLine($"            {Literal(plan.PostgreSql)},");
        AppendLine($"            {Literal(plan.SqlServer)},");
        AppendLine($"            {kinds},");
        AppendLine($"            static (in SqlQuery<{plan.ElementType}> q, global::System.Data.Common.DbParameterCollection p, SqlDialect d) =>");
        AppendLine("            {");
        foreach (var bind in plan.Binds)
            AppendLine($"                {bind}");
        AppendLine("            },");
        AppendLine($"            {Reader(plan.Reader)},");
        RenderLoader(plan.Load);
        // The origin: the tag of the plan's metrics, the comment on its statements. Before's rows are turned back once read; ShapeAsync names the members the client may ask for.
        var shape = plan.HasShapeColumns ? $", shapeColumns: new {Sql}.SqlShapeColumn[] {{ " + Join(plan.ShapeColumns.Select(c => $"new(\"{c.Name}\", {Sql}.SqlParamKind.{c.Kind})")) + " }" : "";
        AppendLine($"            {Literal(plan.Tag)}{(plan.Reversed ? ", reversed: true" : "")}{shape},");
        // No MySQL statement (RETURNING, a table-valued function) or Oracle one (RETURNING rows): the plan says so when asked.
        AppendLine($"            mySql: {LiteralOrNull(plan.MySql)},");
        var tail = new List<string>();
        if (plan.Filters is not null)
            tail.Add($"filters: {Filters(plan.Filters)}"); // Pragmatic's filter families: the statement per caller
        if (plan.PredicateSlots.Count > 0)
            tail.Add($"predicates: new int[] {{ {Join(plan.PredicateSlots.Select(s => s.ToString(System.Globalization.CultureInfo.InvariantCulture)))} }}"); // the slots of the predicates composed at runtime (Matching)
        AppendLine($"            oracle: {LiteralOrNull(plan.Oracle)}{(tail.Count == 0 ? ");" : ",")}");
        for (var i = 0; i < tail.Count; i++)
            AppendLine($"            {tail[i]}{(i == tail.Count - 1 ? ");" : ",")}");
        AppendLine("    }");
        AppendLine();
    }

    private static string Filters(PlanFiltersModel filters)
    {
        static string Text(string value) => value.Length == 0 ? "null" : Literal(value);
        var slots = filters.Slots.Select(s => $"new(typeof(global::{s.Entity}), {Sql}.SqlFilterFamily.{s.Family}, {(s.FilterClass.Length == 0 ? "null" : $"typeof(global::{s.FilterClass})")}, {Text(s.Name)}, {Text(s.Bypass)})");
        return $"new {Sql}.SqlPlanFilters(new {Sql}.SqlFilterSlot[] {{ " + Join(slots) + " }, " + filters.FirstValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    private void RenderHierarchyPlan(HierarchyPlanModel plan)
    {
        AppendLine($"    internal static class {plan.ClassName}");
        AppendLine("    {");
        AppendLine($"        internal static readonly SqlPlan<{plan.EntityType}> Instance = new(");
        AppendLine($"            {Literal(plan.Sqlite)},");
        AppendLine($"            {Literal(plan.PostgreSql)},");
        AppendLine($"            {Literal(plan.SqlServer)},");
        AppendLine($"            new {Sql}.SqlParamKind[] {{ {Sql}.SqlParamKind.{plan.KeyKind} }},");
        AppendLine($"            static (in SqlQuery<{plan.EntityType}> q, global::System.Data.Common.DbParameterCollection p, SqlDialect d) => {{ p[0].Value = {Sql}.SqlValue.ToDatabase(d, q.Slot(0)); }},");
        AppendLine($"            EntityMaps.Read_{plan.ReaderName},");
        AppendLine("            null,");
        AppendLine($"            {Literal(plan.Tag)},");
        AppendLine($"            mySql: {Literal(plan.MySql)},");
        AppendLine($"            oracle: {Literal(plan.Oracle)});");
        AppendLine("    }");
        AppendLine();
    }
}
