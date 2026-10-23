using Pragmatic.Persistence.Sql;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     <c>EntityMaps</c>: the maps, projections and functions registered at module start for the runtime path, the readers the plans call and
///     the private setters the rows are built through. Every registration is one line, as the runtime's constructors take them.
/// </summary>
internal sealed partial class SqlSourceTemplate
{
    private const string Sql = "global::Pragmatic.Persistence.Sql";

    private void RenderMaps(EntityMapsModel maps)
    {
        AppendLine("    file static class EntityMaps");
        AppendLine("    {");
        foreach (var pool in maps.Pools)
            AppendLine($"        internal static readonly {Sql}.SqlInternPool {pool.Field} = {Sql}.SqlInternPool.For({Literal(pool.Key)});");
        AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("        internal static void Register()");
        AppendLine("        {");
        foreach (var map in maps.Maps)
        {
            foreach (var rollUp in map.RollUps)
                AppendLine($"            {Sql}.SqlRollUp.Register(new({Literal(rollUp.ParentTable)}, {Literal(rollUp.ParentKeyColumn)}, {Literal(rollUp.Column)}, {Literal(rollUp.ChildTable)}, {Literal(rollUp.ForeignKey)}, {LiteralOrNull(rollUp.AmountColumn)}){(rollUp.LiveColumn is null ? "" : $" {{ LiveColumn = {Literal(rollUp.LiveColumn)} }}")});");
            AppendLine($"            {Sql}.SqlEntityMap<{map.EntityType}>.Register(new(\"{map.Table}\", {Strings(map.KeyColumns)}, new {Sql}.SqlColumn[] {{ {Join(map.Columns.Select(Column))} }}, new {Sql}.SqlNavigation[] {{ {Join(map.Navigations.Select(Navigation))} }}, new {Sql}.SqlCollection[] {{ {Join(map.Collections.Select(Collection))} }}, new global::System.Func<global::System.Linq.Expressions.LambdaExpression>[] {{ {Join(map.RuleTypes.Select(r => $"static () => new {r}().ToExpression()"))} }}, Read_{map.ReaderName}, static e => e.{map.KeySelectorProperty}, {LiteralOrNull(map.FixedPredicate)}) {{ {MapTail(map)} }});");
        }

        foreach (var projection in maps.Projections)
            AppendLine($"            {Sql}.SqlProjectionMap<{projection.EntityType}, {projection.DtoType}>.Register(new({Strings(projection.Columns)}, {Strings(projection.Joins)}, static (r, d) => {projection.Construct}){ProjectedCollections(projection)});");
        foreach (var function in maps.Functions)
            AppendLine($"            {Sql}.SqlFunctions.Register(((global::System.Func<{Join(function.ParameterTypes.Append(function.ReturnType))}>){function.ContainingType}.{function.Name}).Method, {FunctionRenderer(function)}, isPredicate: {Bool(function.IsPredicate)});");
        AppendLine("        }");
        AppendLine();
        foreach (var reader in maps.Readers)
        {
            AppendLine($"        internal static {reader.EntityType} Read_{reader.Name}(global::System.Data.Common.DbDataReader r, {Sql}.SqlDialect d)");
            AppendLine($"            => {reader.Construct};");
            AppendLine();
        }

        foreach (var setter in maps.Setters)
        {
            AppendLine($"        internal static TRow {setter.Name}<TRow>(TRow row, {setter.Type} value) where TRow : {setter.Owner}");
            AppendLine("        {");
            AppendLine($"            {setter.Name}_Accessor(row, value);");
            AppendLine("            return row;");
            AppendLine("        }");
            AppendLine();
            AppendLine($"        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_{setter.Property}\")]");
            AppendLine($"        private static extern void {setter.Name}_Accessor({setter.Owner} row, {setter.Type} value);");
            AppendLine();
        }

        AppendLine("    }");
        AppendLine();
    }

    /// <summary>The scoped Pragmatic entities' data scope rules, registered with the Sql bridge; nothing when there are none.</summary>
    private void RenderScopeRules(EquatableArray<ScopeRuleModel> rules)
    {
        if (rules.Count == 0)
            return;
        AppendLine("    file static class ScopeRuleRegistrations");
        AppendLine("    {");
        AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        AppendLine("        internal static void Register()");
        AppendLine("        {");
        foreach (var rule in rules)
            AppendLine($"            global::Pragmatic.Persistence.Sql.Pragmatic.CompiledScopeRules.Register(typeof({rule.RuleType}), static rule => {{ var matches = ((global::Pragmatic.Persistence.Scopes.DataScopeRule<{rule.EntityType}>)rule).ToExpression().Compile(preferInterpretation: true); return row => matches(({rule.EntityType})row); }}, readable: {Bool(rule.Readable)});");
        AppendLine("        }");
        AppendLine("    }");
    }

    private static string Column(ColumnMapModel column)
    {
        var toStore = column.Converter is null ? "null" : $"static v => v is null ? null : (object){column.Converter}.ToStore(({column.ConvertedType})v)";
        var initializer = column.IsJson
            ? column.JsonRenames.Count == 0
                ? " { IsJson = true }"
                : $" {{ IsJson = true, JsonNames = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal) {{ {Join(column.JsonRenames.Select(r => $"[{Literal(r.Path)}] = {Literal(r.Name)}"))} }} }}"
            : column.FullTextTable is not null ? $" {{ FullTextTable = {Literal(column.FullTextTable)}, FullTextKey = {Literal(column.FullTextKey!)} }}" : "";
        return $"new {Sql}.SqlColumn(\"{column.Property}\", \"{column.Column}\", {Sql}.SqlParamKind.{column.Kind}, static (r, i, d) => {column.Read}, {toStore}){initializer}";
    }

    /// <summary>The setter takes a missing row as null whatever the property's annotation: a cast of null to a class is null.</summary>
    private static string Navigation(NavigationMapModel navigation)
    {
        var readAt = navigation.TargetConstruct is null
            ? "static (r, o, d) => throw new global::System.InvalidOperationException(\"" + navigation.TargetType + " is not an [Entity] the generator could read\")"
            : $"static (r, o, d) => {navigation.TargetConstruct}";
        var composite = navigation.ForeignKeyColumns.Count == 0 ? "" : $" {{ ForeignKeyColumns = {Strings(navigation.ForeignKeyColumns)} }}";
        return $"new {Sql}.SqlNavigation(\"{navigation.Property}\", \"{navigation.JoinColumn}\", {Bool(navigation.Required)}, typeof({navigation.TargetType}), {readAt}, static e => (({navigation.OwnerType})e).{navigation.Property}, static (e, v) => (({navigation.OwnerType})e).{navigation.Property} = ({navigation.TargetType})v!){composite}";
    }

    private static string Collection(CollectionMapModel collection)
    {
        var childKey = collection.CompositeChildKey
            ? $"new {Sql}.SqlCompositeKey({Join(collection.ChildKeyProperties.Select(k => $"(({collection.ChildType})c).{k}"))})"
            : $"((({collection.ChildType})c).{collection.ChildKeyProperties[0]})";
        var initializers = new List<string>();
        if (collection.LinkTable is not null)
            initializers.Add($"LinkTable = {Literal(collection.LinkTable)}, LinkChildColumn = {Literal(collection.LinkChildColumn!)}");
        if (collection.LinkChildColumns.Count > 0)
            initializers.Add($"LinkChildColumns = {Strings(collection.LinkChildColumns)}");
        if (collection.ForeignKeyColumns.Count > 0)
            initializers.Add($"ForeignKeyColumns = {Strings(collection.ForeignKeyColumns)}");
        var link = initializers.Count == 0 ? "" : " { " + Join(initializers) + " }";
        return $"new {Sql}.SqlCollection(\"{collection.Property}\", typeof({collection.ChildType}), \"{collection.ForeignKeyColumn}\", static c => {childKey}, static e => (({collection.OwnerType})e).{collection.Property}, static (e, rows) => (({collection.OwnerType})e).{collection.Property} = rows.ConvertAll(static x => ({collection.ChildType})x)){link}";
    }

    /// <summary>How the map writes, then what its runtime path's reads need: the soft-delete column, the caller's families, a composite parent's key.</summary>
    private static string MapTail(EntityMapModel map)
    {
        var tail = map.WriteMode switch
        {
            EntityWriteMode.ReadOnly => "ReadOnly = true",
            EntityWriteMode.Dispatch => "Dispatch = true",
            EntityWriteMode.DispatchWithWriter => $"Dispatch = true, Writer = {Writer(map.Writer!)}",
            _ => $"Writer = {Writer(map.Writer!)}",
        };
        if (map.SoftDeleteColumn is not null)
            tail += $", SoftDeleteColumn = {Literal(map.SoftDeleteColumn)}{(map.SoftDeleteCascade ? ", SoftDeleteCascade = true" : "")}";
        if (map.CallerFiltered)
            tail += ", CallerFiltered = true";
        if (map.CompositeKeyProperties.Count > 0)
            tail += $", CompositeKeyOf = static e => new {Sql}.SqlCompositeKey({Join(map.CompositeKeyProperties.Select(k => $"(({map.EntityType})e).{k}"))})";
        return tail;
    }

    private static string Writer(WriterModel writer)
    {
        var columns = writer.Columns;
        var versioned = writer.VersionIndex >= 0;
        var keyText = writer.KeyProperties.Count == 1
            ? $"static e => e.{writer.KeyProperties[0]}"
            : $"static e => \"(\" + {string.Join(" + \", \" + ", writer.KeyProperties.Select(k => $"e.{k}"))} + \")\"";
        var identity = writer.Identity is null
            ? "null"
            : $"new {Sql}.SqlIdentity<{writer.EntityType}>(static (e, p, o, d) => {{ {Binds(writer.Identity.BindsWithoutKey)} }}, static (e, r, d) => e.{writer.Identity.KeyProperty} = {writer.Identity.KeyRead})";
        var casts = columns.Any(c => c.Cast is not null) ? "new string?[] { " + Join(columns.Select(c => LiteralOrNull(c.Cast))) + " }" : "null";
        var generated = writer.Generated.Count == 0
            ? "null"
            : $"new {Sql}.SqlGenerated<{writer.EntityType}>({Strings(writer.Generated.Select(g => g.Column))}, {Bool(writer.GeneratedOnUpdates)}, static (e, r, d) => {{ {string.Join(" ", writer.Generated.Select(g => $"e.{g.Property} = {g.Read};"))} }})";
        var text = $"new {Sql}.SqlWriter<{writer.EntityType}>(\"{writer.Table}\", {Strings(columns.Select(c => c.Column))}, new {Sql}.SqlParamKind[] {{ {Join(columns.Select(c => $"{Sql}.SqlParamKind.{c.Kind}"))} }}, new int[] {{ {Join(writer.KeyIndexes.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)))} }}, {writer.VersionIndex}, static (e, p, o, d) => {{ {Binds(columns.Select(c => c.Bind))} }}, static (e, p, o, d) => {{ {Binds(writer.KeyBinds)} }}, {keyText}, {(versioned ? $"static e => e.{writer.VersionProperty}" : "null")}, {(versioned ? $"static e => e.{writer.VersionProperty}++" : "null")}, {(versioned ? $"static (e, r, d) => e.{writer.VersionProperty} = {writer.VersionRead}" : "null")}, {identity}, {casts}, {Strings(columns.Select(c => c.Property))}, static (e, c, p, i, d) => {{ switch (c) {{ {string.Join(" ", columns.Select((c, i) => $"case {i}: p[i].Value = {Sql}.SqlValue.ToDatabase(d, {c.Bind}); break;"))} }} }}, {generated}, {(writer.Part is null ? "null" : Writer(writer.Part))}, static (e, c, d) => c switch {{ {Join(columns.Select((c, i) => $"{i} => {Sql}.SqlValue.ToDatabase(d, {c.Bind})"))}, _ => null }})";
        var initializers = new List<string>();
        if (writer.Stamps is not null)
            initializers.Add($"Stamps = {Stamps(writer.Stamps)}");
        if (writer.HasNativeVersion)
            initializers.Add($"NativeVersion = new {Sql}.SqlNativeVersion<{writer.EntityType}>(static (e, p, o, d) => {{ {Binds(writer.NativeVersionBinds)} }})");
        return initializers.Count == 0 ? text : text + " { " + Join(initializers) + " }";
    }

    /// <summary>Each value bound at its parameter, in order from the writer's offset.</summary>
    private static string Binds(IEnumerable<string> binds)
        => string.Join(" ", binds.Select((bind, i) => $"p[o + {i}].Value = {Sql}.SqlValue.ToDatabase(d, {bind});"));

    private static string Stamps(StampsModel stamps)
    {
        static string Set(StampColumnModel? column, string value) => column is null ? "" : $"e.{column.Property} = {value}; ";
        static string Clock(StampColumnModel column) => column.IsDateTime ? "now.UtcDateTime" : "now";
        static string Setter(StampColumnModel? at, StampColumnModel? by)
            => at is null && by is null ? "null" : $"static (e, now, user) => {{ {Set(at, at is null ? "" : Clock(at))}{Set(by, "user")}}}";
        static string Names(params StampColumnModel?[] columns) => "new string[] { " + string.Join(", ", columns.Where(c => c is not null).Select(c => $"\"{c!.Column}\"")) + " }";
        var deleted = stamps.IsDeleted is null
            ? "null"
            : $"static (e, deleted, now, user) => {{ {Set(stamps.IsDeleted, "deleted")}{Set(stamps.DeletedAt, stamps.DeletedAt is null ? "" : $"deleted ? {Clock(stamps.DeletedAt)} : null")}{Set(stamps.DeletedBy, "deleted ? user : null")}}}";
        return $"new {Sql}.SqlStamps<{stamps.EntityType}>({Setter(stamps.CreatedAt, stamps.CreatedBy)}, {Setter(stamps.UpdatedAt, stamps.UpdatedBy)}, {deleted}, {Names(stamps.CreatedAt, stamps.CreatedBy)}, {Names(stamps.UpdatedAt, stamps.UpdatedBy)}, {Names(stamps.IsDeleted, stamps.DeletedAt, stamps.DeletedBy)})";
    }

    /// <summary>The DTO's collection members: the child DTO's own registration gives its columns and reader; here the key the loader groups on and the assignment.</summary>
    private static string ProjectedCollections(ProjectionMapModel projection)
    {
        if (projection.Collections.Count == 0)
            return "";
        var dto = projection.DtoType;
        return $" {{ Collections = new {Sql}.SqlProjectedCollection[] {{ " + Join(projection.Collections.Select(c => projection.CompositeKey
            ? $"new {Sql}.SqlProjectedCollection(\"{c.Property}\", typeof({c.ChildEntityType}), typeof({c.ChildDtoType}), \"{c.ForeignKeyColumn}\", static p => new {Sql}.SqlCompositeKey({Join(projection.CompositeKeyProperties.Select(k => $"(({dto})p).{k}"))}), static (p, rows) => (({dto})p).{c.Property} = rows.ConvertAll(static x => ({c.ChildDtoType})x)) {{ ForeignKeyColumns = {Strings(c.ForeignKeyColumns)} }}"
            : $"new {Sql}.SqlProjectedCollection(\"{c.Property}\", typeof({c.ChildEntityType}), typeof({c.ChildDtoType}), \"{c.ForeignKeyColumn}\", static p => (({dto})p).{projection.KeyProperty}, static (p, rows) => (({dto})p).{c.Property} = rows.ConvertAll(static x => ({c.ChildDtoType})x))")) + " } }";
    }

    /// <summary>The method's renderer: an arm per dialect, a dialect without a rendering throwing.</summary>
    private static string FunctionRenderer(FunctionRegistrationModel function)
    {
        var arms = function.Arms.Select(arm => arm.Concatenation is null
            ? $"{Sql}.SqlDialect.{arm.Dialect} => throw new global::System.NotSupportedException(\"{function.Name} has no rendering for {arm.Dialect}\")"
            : $"{Sql}.SqlDialect.{arm.Dialect} => {arm.Concatenation}");
        return $"static (d, a) => d switch {{ {Join(arms)}, _ => throw new global::System.NotSupportedException(\"{function.Name}: unknown dialect\") }}";
    }

    private static string Strings(IEnumerable<string> values) => "new string[] { " + Join(values.Select(Literal)) + " }";

    private static string Join(IEnumerable<string> parts) => string.Join(", ", parts);

    private static string Literal(string text) => SqlInterceptorGenerator.Literal(text);

    private static string LiteralOrNull(string? text) => text is null ? "null" : Literal(text);

    private static string Bool(bool value) => value ? "true" : "false";
}
