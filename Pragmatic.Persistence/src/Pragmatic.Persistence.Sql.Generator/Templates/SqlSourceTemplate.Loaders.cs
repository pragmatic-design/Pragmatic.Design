using System.Linq;
using Pragmatic.Persistence.Sql.Generator.Models;

namespace Pragmatic.Persistence.Sql.Generator.Templates;

/// <summary>
///     A plan's reader and loader. The loader reads the companion statements' result sets in the order they were built and hangs the children on
///     their parents: a collection under an included to-one object is loaded for the objects the rows carry, a collection under a collection
///     after its parent's rows, which are its parents.
/// </summary>
internal sealed partial class SqlSourceTemplate
{
    /// <summary>The reader lambda (or method group) that shapes one row; <c>null</c> for a scalar.</summary>
    private static string Reader(PlanReaderModel reader) => reader.Kind switch
    {
        PlanReaderKind.Method => $"EntityMaps.Read_{reader.Text}",
        PlanReaderKind.Lambda => $"static (r, d) => {reader.Text}",
        PlanReaderKind.Include => $"static (r, d) => {{ var e = EntityMaps.Read_{reader.Text}(r, d);{string.Concat(reader.Includes.Select(i => $" e.{i.Property} = {i.Hydrate};"))} return e; }}",
        _ => "null",
    };

    /// <summary>The plan's loader argument: <c>null</c> when it has no companion statement.</summary>
    private void RenderLoader(LoaderModel? loader)
    {
        if (loader is null)
        {
            AppendLine("            null,");
            return;
        }

        AppendLine($"            static async (global::System.Data.Common.DbDataReader r, SqlDialect d, global::System.Collections.Generic.List<{loader.ParentsType}> parents, global::System.Threading.CancellationToken ct) =>");
        AppendLine("            {");
        foreach (var step in loader.Steps)
            RenderLoadStep(step);
        AppendLine("            },");
    }

    private void RenderLoadStep(LoadStepModel step)
    {
        var name = step.Name;
        const string list = "global::System.Collections.Generic.List";
        const string next = "await r.NextResultAsync(ct).ConfigureAwait(false);";
        const string read = "await r.ReadAsync(ct).ConfigureAwait(false)";
        switch (step)
        {
            case CollectionLoadModel c:
                AppendLine($"                {next}");
                AppendLine($"                global::System.Func<global::System.Data.Common.DbDataReader, SqlDialect, {c.ChildType}> {name}_read = {Reader(c.Reader)};");
                AppendLine($"                var {name}_rows = new {list}<{c.ChildType}>();");
                AppendLine($"                var {name}_groups = new global::System.Collections.Generic.Dictionary<{c.GroupType}, {list}<{c.ChildType}>>();");
                if (c.LinkKey is not null)
                {
                    // Through a join table: the parent's key is the row's last column, read beside the child.
                    AppendLine($"                while ({read}) {{ var child = {name}_read(r, d); {name}_rows.Add(child); var key = {c.LinkKey}; if (!{name}_groups.TryGetValue(key, out var group)) {name}_groups[key] = group = new(); group.Add(child); }}");
                }
                else
                {
                    AppendLine($"                while ({read}) {name}_rows.Add({name}_read(r, d));");
                    AppendLine($"                foreach (var child in {name}_rows) {{ if (!{name}_groups.TryGetValue({c.ChildKey}, out var group)) {name}_groups[{c.ChildKey}] = group = new(); group.Add(child); }}");
                }

                AppendLine($"                foreach (var parent in {c.Parents}) parent.{c.Property} = {name}_groups.TryGetValue({c.ParentKey}, out var {name}_mine) ? {name}_mine : new();");
                break;
            case ToOneLoadModel o:
                AppendLine($"                var {name}_parents = new {list}<{o.TargetType}>({o.Parents}.Count);");
                AppendLine($"                foreach (var parent in {o.Parents}) if (parent.{o.Property} is {{ }} {name}_one) {name}_parents.Add({name}_one);");
                break;
            case ProjectedLoadModel p:
                var nested = p.Children.Count > 0;
                AppendLine($"                {next}");
                AppendLine($"                var {name}_groups = new global::System.Collections.Generic.Dictionary<{p.KeyType}, {list}<{p.ChildDtoType}>>();");
                if (nested)
                    AppendLine($"                var {name}_rows = new {list}<{p.ChildDtoType}>();");
                AppendLine($"                while ({read}) {{ var row = {p.Construct}; var key = {p.KeyRead}; if (!{name}_groups.TryGetValue(key, out var group)) {name}_groups[key] = group = new(); group.Add(row);{(nested ? $" {name}_rows.Add(row);" : "")} }}");
                AppendLine($"                foreach (var parent in {p.Parents}) parent.{p.Property} = {name}_groups.TryGetValue({p.ParentKey}, out var {name}_mine) ? {name}_mine : new();");
                break;
        }

        foreach (var child in step.Children)
            RenderLoadStep(child);
    }
}
