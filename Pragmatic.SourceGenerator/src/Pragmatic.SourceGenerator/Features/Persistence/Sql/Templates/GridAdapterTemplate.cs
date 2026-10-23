using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     A grid adapter of Pragmatic's on an engine: <c>ToSpecification(filter)</c>, the DevExpress filter as a specification (a group, a negation,
///     a clause; every part a predicate or a refusal, never nothing, as Pragmatic's adapter answers); <c>LoadAsync(session, options)</c>, the page
///     read through the session's filters with its sort, its skip and take, its total when asked. The clauses are <c>Spec&lt;T&gt;.Where</c>
///     lambdas over typed values, one per field and operator, so EF Core reads the same specification through <c>ToExpression()</c>.
/// </summary>
internal sealed partial class GridAdapterTemplate(GridAdapterModel model) : CSharpTemplate
{
    private const string Adapters = "global::Pragmatic.Persistence.Query.Adapters";

    private string Spec => $"global::Pragmatic.Specification.Spec<{model.EntityType}>";

    private string Specification => $"global::Pragmatic.Specification.Specification<{model.EntityType}>";

    protected override string? GeneratorName => model.Package;

    protected override string? SourceInfo => $"the grid adapter {model.TypeName} on the {model.Engine}";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.TypeName}");
        AppendLine("{");
        AppendLine($"    /// <summary>The DevExpress filter as a specification of {model.EntityName}: no filter is every row, a part that cannot apply is refused.</summary>");
        AppendLine($"    public static {Specification} ToSpecification(global::System.Collections.Generic.IReadOnlyList<object?>? filter)");
        AppendLine("    {");
        AppendLine("        var terms = 0;");
        AppendLine($"        return filter is {{ Count: > 0 }} ? Parse(filter, ref terms) : {Spec}.True;");
        AppendLine("    }");
        AppendLine();
        RenderLoad();
        RenderParse();
        RenderClause();
        foreach (var field in model.Fields.Where(f => f.Filterable))
            RenderFieldFilter(field);
        RenderOrdering();
        RenderHelpers();
        AppendLine("}");
    }

    /// <summary><c>LoadAsync</c>: the filter parsed, the sort ordered, the page read inside the session's filters, its total when asked.</summary>
    private void RenderLoad()
    {
        var load = model.Load;
        var query = $"session.Query<{model.EntityType}>().Matching(predicate)";
        AppendLine("    /// <summary>The page a DevExpress grid asked for, read inside the session's filters: its rows, and its total when RequireTotalCount.</summary>");
        AppendLine($"    public static async global::System.Threading.Tasks.Task<{load.Page}> LoadAsync({load.Session} session, {Adapters}.DevExpressLoadOptions options, global::System.Threading.CancellationToken ct = default)");
        AppendLine("    {");
        AppendLine("        // Every clause and every sort is a term; past the bound the request is refused before anything is built (GridFilterTooLargeException).");
        AppendLine("        var terms = 0;");
        AppendLine($"        var specification = options.Filter is {{ Count: > 0 }} filter ? Parse(filter, ref terms) : {Spec}.True;");
        AppendLine("        var ordering = Ordering(options.Sort, ref terms);");
        AppendLine($"        var predicate = {load.ToPredicate}(specification);");
        AppendLine("        var skip = options.Skip ?? 0;");
        AppendLine("        var rows = options.Take is { } take");
        AppendLine($"            ? await {query}.OrderedBy(ordering).Skip(skip).Take(take).ToListAsync(ct)");
        AppendLine($"            : await {query}.OrderedBy(ordering).Skip(skip).ToListAsync(ct);");
        var count = load.CountsInLong ? $"checked((int)await {query}.CountAsync(ct))" : $"await {query}.CountAsync(ct)";
        AppendLine($"        int? total = options.RequireTotalCount ? {count} : null;");
        AppendLine($"        return new {load.Page}(rows, total);");
        AppendLine("    }");
        AppendLine();
    }

    private void RenderParse()
    {
        AppendLine($"    private static {Specification} Parse(global::System.Collections.Generic.IReadOnlyList<object?> filter, ref int terms)");
        AppendLine("    {");
        if (!model.Nested)
        {
            AppendLine("        // Without nested filters a filter is one clause; a group is refused, not ignored.");
            AppendLine("        if (filter.Count != 3 || Unwrap(filter[0]) is not string field)");
            AppendLine("            throw Malformed();");
            AppendLine("        terms = CountedTerms(terms + 1);");
            AppendLine("        return Clause(field, Unwrap(filter[1]) as string, Unwrap(filter[2]));");
            AppendLine("    }");
            AppendLine();
            return;
        }

        AppendLine("        // A group of one: the clause or the group it holds. A filter builder wraps a single condition this way.");
        AppendLine("        if (filter.Count == 1 && Group(filter[0]) is { } only)");
        AppendLine("            return Parse(only, ref terms);");
        AppendLine("        if (filter.Count == 2 && Unwrap(filter[0]) is \"!\" && Group(filter[1]) is { } negated)");
        AppendLine("            return !Parse(negated, ref terms);");
        AppendLine("        if (filter.Count == 3 && Unwrap(filter[0]) is string field && field is not (\"and\" or \"or\" or \"!\"))");
        AppendLine("        {");
        AppendLine("            terms = CountedTerms(terms + 1);");
        AppendLine("            return Clause(field, Unwrap(filter[1]) as string, Unwrap(filter[2]));");
        AppendLine("        }");
        AppendLine();
        AppendLine("        // A composite: [group, \"and\"/\"or\", group, ...], ending on a group.");
        AppendLine("        if (filter.Count >= 3 && filter.Count % 2 == 1)");
        AppendLine("        {");
        AppendLine("            var combined = Parse(GroupAt(filter, 0), ref terms);");
        AppendLine("            for (var i = 1; i < filter.Count; i += 2)");
        AppendLine("            {");
        AppendLine("                var connective = (Unwrap(filter[i]) as string)?.ToLowerInvariant();");
        AppendLine("                if (connective is not (\"and\" or \"or\"))");
        AppendLine("                    throw Malformed();");
        AppendLine("                var next = Parse(GroupAt(filter, i + 1), ref terms);");
        AppendLine("                combined = connective == \"and\" ? combined & next : combined | next;");
        AppendLine("            }");
        AppendLine();
        AppendLine("            return combined;");
        AppendLine("        }");
        AppendLine();
        AppendLine("        throw Malformed();");
        AppendLine("    }");
        AppendLine();
    }

    /// <summary>The field first, so neither the operator nor the value says whether a withheld column exists.</summary>
    private void RenderClause()
    {
        AppendLine($"    private static {Specification} Clause(string field, string? op, object? value)");
        AppendLine("    {");
        AppendLine("        if (!IsFilterable(field))");
        AppendLine("            throw RejectedField(field);");
        AppendLine("        if (op is null)");
        AppendLine("            throw Unsupported(field, op);");
        AppendLine("        return field.ToLowerInvariant() switch");
        AppendLine("        {");
        foreach (var field in model.Fields.Where(f => f.Filterable))
            AppendLine($"            \"{field.Json.ToLowerInvariant()}\" => Filter_{field.Property}(field, op, value),");
        AppendLine("            _ => throw RejectedField(field),");
        AppendLine("        };");
        AppendLine("    }");
        AppendLine();
    }

    private void RenderFieldFilter(GridFieldModel field)
    {
        var e = $"e.{field.Property}";
        AppendLine($"    private static {Specification} Filter_{field.Property}(string field, string op, object? value)");
        AppendLine("    {");
        if (field.AdmittedOperators.Count > 0)
        {
            AppendLine($"        if (op.ToLowerInvariant() is not ({string.Join(" or ", field.AdmittedOperators.Select(t => PragmaticNames.Literal(t)))}))");
            AppendLine("            throw Unsupported(field, op);");
        }

        // Against null only equality and inequality mean something, on a column that can hold it.
        AppendLine("        if (Unwrap(value) is null)");
        if (field.Kind != GridFieldKind.String && !field.Nullable)
        {
            AppendLine("            throw Unusable(field);");
        }
        else
        {
            AppendLine("            return op.ToLowerInvariant() switch");
            AppendLine("            {");
            AppendLine($"                \"=\" or \"==\" or \"equals\" => {Spec}.Where(e => {e} == null),");
            AppendLine($"                \"<>\" or \"!=\" or \"notequals\" => {Spec}.Where(e => {e} != null),");
            AppendLine("                _ => throw Unusable(field),");
            AppendLine("            };");
        }

        // The value read as the field's type: a refusal when it is not one.
        string[]? read = field.Kind switch
        {
            GridFieldKind.String => ["var typed = Text(value);", "if (typed is null) throw Unusable(field);"],
            GridFieldKind.Boolean or GridFieldKind.Numeric => [$"if (!TryConvert<{field.Type}>(value, out var typed)) throw Unusable(field);"],
            GridFieldKind.DateTime when field.Type == "global::System.DateTimeOffset" => ["if (!TryDateTimeOffset(value, out var typed)) throw Unusable(field);"],
            GridFieldKind.DateTime => [$"if (!TryConvert<{field.Type}>(value, out var typed)) throw Unusable(field);"],
            GridFieldKind.Guid => ["if (!TryGuid(value, out var typed)) throw Unusable(field);"],
            GridFieldKind.Enum => [$"if (!TryEnum<{field.Type}>(value, out var typed)) throw Unusable(field);"],
            _ => null,
        };
        if (read is null)
        {
            AppendLine("        throw Unsupported(field, op);");
            AppendLine("    }");
            AppendLine();
            return;
        }

        foreach (var line in read)
            AppendLine("        " + line);
        AppendLine("        return op.ToLowerInvariant() switch");
        AppendLine("        {");
        var arms = new List<(string Tokens, string Condition)>
        {
            ("\"=\" or \"==\" or \"equals\"", $"{e} == typed"),
            ("\"<>\" or \"!=\" or \"notequals\"", $"{e} != typed"),
        };
        if (field.Kind == GridFieldKind.String)
        {
            arms.Add(("\"contains\"", $"{e} != null && {e}.Contains(typed)"));
            arms.Add(("\"notcontains\"", $"{e} == null || !{e}.Contains(typed)"));
            arms.Add(("\"startswith\"", $"{e} != null && {e}.StartsWith(typed)"));
            arms.Add(("\"endswith\"", $"{e} != null && {e}.EndsWith(typed)"));
        }
        else if (field.Kind is GridFieldKind.Numeric or GridFieldKind.DateTime)
        {
            arms.Add(("\">\" or \"gt\"", $"{e} > typed"));
            arms.Add(("\">=\" or \"gte\"", $"{e} >= typed"));
            arms.Add(("\"<\" or \"lt\"", $"{e} < typed"));
            arms.Add(("\"<=\" or \"lte\"", $"{e} <= typed"));
        }

        foreach (var (tokens, condition) in arms)
            AppendLine($"            {tokens} => {Spec}.Where(e => {condition}),");
        AppendLine("            _ => throw Unsupported(field, op),");
        AppendLine("        };");
        AppendLine("    }");
        AppendLine();
    }

    /// <summary>The sort: a column the adapter does not sort on is passed over, as Pragmatic's adapter passes it; the key last is the fallback.</summary>
    private void RenderOrdering()
    {
        var ordering = model.Ordering;
        AppendLine($"    private static {ordering.Ordering} Ordering(global::System.Collections.Generic.IReadOnlyList<{Adapters}.DevExpressSortDescriptor>? sort, ref int terms)");
        AppendLine("    {");
        AppendLine($"        var ordering = {ordering.Ordering}.By(__OrderBy_PersistenceId);");
        AppendLine("        if (sort is null)");
        AppendLine("            return ordering;");
        AppendLine("        terms = CountedTerms(terms + sort.Count);");
        AppendLine("        foreach (var descriptor in sort)");
        AppendLine("        {");
        AppendLine("            var key = descriptor.Selector?.ToLowerInvariant() switch");
        AppendLine("            {");
        foreach (var field in model.Fields.Where(f => f.Sortable))
            AppendLine($"                \"{field.Json.ToLowerInvariant()}\" => __OrderBy_{field.Property},");
        AppendLine("                _ => null,");
        AppendLine("            };");
        AppendLine("            if (key is not null)");
        AppendLine("                ordering = ordering.Then(key, descriptor.Desc);");
        AppendLine("        }");
        AppendLine();
        AppendLine("        return ordering;");
        AppendLine("    }");
        AppendLine();
        foreach (var key in ordering.Keys)
            AppendLine($"    private static readonly {ordering.Key} __OrderBy_{key.Key} = new({string.Join(", ", key.Texts.Select(SqlInterceptorGenerator.Literal))});");
        AppendLine();
    }
}
