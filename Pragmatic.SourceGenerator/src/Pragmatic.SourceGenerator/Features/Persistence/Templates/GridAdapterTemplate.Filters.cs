using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     How the generated adapter reads a grid's filter, and refuses what it cannot apply.
/// </summary>
/// <remarks>
///     ⚠️ <b>Every part of a filter becomes a predicate or a refusal, never nothing (#318).</b> A filter restricts, so
///     a clause left out answers with more rows than the client asked for, under a success status, and the grid shows
///     them as the filtered result. The generated dispatcher skipped an unknown field, an operator the column did not
///     support and a value that did not convert, and a composite skipped the group that came back empty. The refusals
///     are the runtime adapters' own: <c>GridFieldRejectedException</c> for a field, <c>GridFilterRejectedException</c>
///     for a clause, each checked in that order so the reason never says whether a withheld column exists.
/// </remarks>
internal sealed partial class GridAdapterTemplate
{
    private const string Adapters = "global::Pragmatic.Persistence.Query.Adapters";

    private void RenderNestedFilterMethod()
    {
        AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplyNestedFilter(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("IReadOnlyList<object?> filter,");
        AppendLine("ref int terms)");
        DecreaseIndent();
        Block(() => AppendLine("return query.Where(ParseFilterExpression(filter, ref terms));"));
        AppendLine();

        // Recursive filter expression parser
        AppendLine($"private static Expression<Func<{_model.EntityTypeFullName}, bool>> ParseFilterExpression(IReadOnlyList<object?> filter, ref int terms)");
        Block(() =>
        {
            AppendLine("// A group of one: the clause or the group it holds. A filter builder wraps a single condition this way.");
            AppendLine("if (filter.Count == 1 && Group(filter[0]) is { } only)");
            IncreaseIndent();
            AppendLine("return ParseFilterExpression(only, ref terms);");
            DecreaseIndent();
            AppendLine();
            AppendLine("// A negation: [\"!\", group]");
            AppendLine("if (filter.Count == 2 && Unwrap(filter[0]) is \"!\" && Group(filter[1]) is { } negated)");
            Block(() =>
            {
                AppendLine("var inner = ParseFilterExpression(negated, ref terms);");
                AppendLine($"return Expression.Lambda<Func<{_model.EntityTypeFullName}, bool>>(Expression.Not(inner.Body), inner.Parameters);");
            });
            AppendLine();
            AppendLine("// Simple filter: [field, op, value]");
            AppendLine("// ⚠️ Unwrapped first: a filter that arrived over HTTP is a list of JsonElement,");
            AppendLine("// not of string, so testing `is string` here matched nothing and the whole");
            AppendLine("// branch was dead — the query came back unfiltered.");
            AppendLine("if (filter.Count == 3 && Unwrap(filter[0]) is string field && field is not (\"and\" or \"or\" or \"!\"))");
            Block(() =>
            {
                AppendLine("terms = CountedTerms(terms + 1);");
                AppendLine("return CreateFieldFilter(field, Unwrap(filter[1]) as string, Unwrap(filter[2]));");
            });
            AppendLine();
            AppendLine("// Composite filter: [group, \"and\"/\"or\", group, ...], ending on a group");
            AppendLine("if (filter.Count >= 3 && filter.Count % 2 == 1)");
            Block(() =>
            {
                AppendLine("var combined = ParseFilterExpression(GroupAt(filter, 0), ref terms);");
                AppendLine("for (var i = 1; i < filter.Count; i += 2)");
                Block(() =>
                {
                    AppendLine("var connective = (Unwrap(filter[i]) as string)?.ToLowerInvariant();");
                    AppendLine("if (connective is not (\"and\" or \"or\"))");
                    IncreaseIndent();
                    AppendLine("throw Malformed();");
                    DecreaseIndent();
                    AppendLine();
                    AppendLine("combined = CombineExpressions(combined, ParseFilterExpression(GroupAt(filter, i + 1), ref terms), connective);");
                });
                AppendLine();
                AppendLine("return combined;");
            });
            AppendLine();
            AppendLine("throw Malformed();");
        });
        AppendLine();

        // A clause or a group, as a list: a nested group that arrived as JSON is an array element, not a list.
        AppendLine("private static IReadOnlyList<object?>? Group(object? value) => value switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("global::System.Text.Json.JsonElement { ValueKind: global::System.Text.Json.JsonValueKind.Array } json => json.EnumerateArray().Select(e => (object?)e).ToList(),");
        AppendLine("IReadOnlyList<object?> list => list,");
        AppendLine("IList list => list.Cast<object?>().ToList(),");
        AppendLine("_ => null,");
        DecreaseIndent();
        AppendLine("};");
        AppendLine();
        AppendLine("private static IReadOnlyList<object?> GroupAt(IReadOnlyList<object?> filter, int index)");
        IncreaseIndent();
        AppendLine("=> Group(filter[index]) ?? throw Malformed();");
        DecreaseIndent();
        AppendLine();

        RenderExpressionCombiner();
    }

    private void RenderSimpleFilterMethod()
    {
        AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplySimpleFilter(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("IReadOnlyList<object?> filter)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("// Without nested filters a filter is one clause; a group is refused, not ignored.");
            AppendLine("if (filter.Count != 3 || Unwrap(filter[0]) is not string field)");
            IncreaseIndent();
            AppendLine("throw Malformed();");
            DecreaseIndent();
            AppendLine();
            AppendLine("return query.Where(CreateFieldFilter(field, Unwrap(filter[1]) as string, Unwrap(filter[2])));");
        });
    }

    private void RenderPrimeNGFilterMethod()
    {
        AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplyPrimeNGFilter(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("string field,");
        AppendLine("PrimeNGFilterMetadata filterMeta)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("if (!IsFilterable(field))");
            IncreaseIndent();
            AppendLine("throw RejectedField(field);");
            DecreaseIndent();
            AppendLine();
            AppendLine("var matchMode = filterMeta.MatchMode?.ToLowerInvariant() ?? \"contains\";");
            AppendLine();
            AppendLine("// An in is one equality per value, a between [min, max]: each value converted on its own, as the");
            AppendLine("// runtime adapter does (#317).");
            AppendLine("if (matchMode == \"in\")");
            Block(() =>
            {
                AppendLine("if (Elements(filterMeta.Value) is not { Count: > 0 } values)");
                IncreaseIndent();
                AppendLine("throw Unusable(field);");
                DecreaseIndent();
                AppendLine();
                AppendLine($"Expression<Func<{_model.EntityTypeFullName}, bool>>? any = null;");
                AppendLine("foreach (var value in values)");
                Block(() =>
                {
                    AppendLine("var equal = CreateFieldFilter(field, \"=\", value);");
                    AppendLine($"any = any is null ? equal : {Adapters}.GridPredicate.Or(any, equal);");
                });
                AppendLine();
                AppendLine("return query.Where(any!);");
            });
            AppendLine();
            AppendLine("if (matchMode == \"between\")");
            Block(() =>
            {
                AppendLine("if (Elements(filterMeta.Value) is not { Count: 2 } bounds)");
                IncreaseIndent();
                AppendLine("throw Unusable(field);");
                DecreaseIndent();
                AppendLine();
                AppendLine($"return query.Where({Adapters}.GridPredicate.And(CreateFieldFilter(field, \">=\", bounds[0]), CreateFieldFilter(field, \"<=\", bounds[1])));");
            });
            AppendLine();
            AppendLine("// A match mode the adapter does not know is refused: read as equals, dateBefore answered with the");
            AppendLine("// rows on that date.");
            AppendLine("var op = matchMode switch");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("\"equals\" => \"=\",");
            AppendLine("\"notequals\" => \"<>\",");
            AppendLine("\"lt\" => \"<\",");
            AppendLine("\"lte\" => \"<=\",");
            AppendLine("\"gt\" => \">\",");
            AppendLine("\"gte\" => \">=\",");
            AppendLine("\"contains\" => \"contains\",");
            AppendLine("\"startswith\" => \"startswith\",");
            AppendLine("\"endswith\" => \"endswith\",");
            AppendLine("\"notcontains\" => \"notcontains\",");
            AppendLine($"_ => throw new {Adapters}.GridFilterRejectedException(field, {Adapters}.GridFilterRejection.UnsupportedOperator, filterMeta.MatchMode),");
            DecreaseIndent();
            AppendLine("};");
            AppendLine();
            AppendLine("return query.Where(CreateFieldFilter(field, op, filterMeta.Value));");
        });
    }

    /// <summary>
    ///     The grid's single search box: one value matched against every text field, joined by OR.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A typed lambda, not an expression built by hand — <c>typeof(string).GetMethod("Contains")</c>
    ///         and <c>Expression.Property(parameter, "Name")</c> would be reflection, at run time, in the
    ///         consumer's application, emitted by an attribute whose own documentation promises "compile-time
    ///         optimized, strongly-typed LINQ expressions". The switch knows the field at compile time, so a
    ///         typed lambda says the same thing with none of it. <c>DevExpressAdapter</c> in the runtime
    ///         captures its MethodInfo from a typed lambda the same way.
    ///     </para>
    ///     <para>
    ///         And nothing would catch it: the reflection ratchet skips <c>.g.cs</c> because "the
    ///         generator is measured by what it emits", then skips generator projects because "a
    ///         generator runs at compile time where reflection costs nothing" — so what a template puts
    ///         into an application is measured by neither half.
    ///     </para>
    ///     <para>
    ///         A column the adapter does not filter on is refused; a filterable one that is not text is passed
    ///         over, since a grid lists every column it shows. A search that reaches no text column restricts
    ///         nothing, and is refused rather than answered with every row (#318).
    ///     </para>
    /// </remarks>
    private void RenderGlobalFilterMethod()
    {
        var stringFields = _model.Fields
            .Where(f => f.Filterable && f.TypeCategory == PropertyTypeCategory.String)
            .ToList();

        AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplyGlobalFilter(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("string searchValue,");
        AppendLine("IReadOnlyList<string> fields)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine($"global::System.Linq.Expressions.Expression<Func<{_model.EntityTypeFullName}, bool>>? combined = null;");
            AppendLine();
            AppendLine("foreach (var field in fields)");
            Block(() =>
            {
                AppendLine("if (!IsFilterable(field))");
                IncreaseIndent();
                AppendLine("throw RejectedField(field);");
                DecreaseIndent();

                if (stringFields.Count == 0)
                    return;

                AppendLine();
                AppendLine($"global::System.Linq.Expressions.Expression<Func<{_model.EntityTypeFullName}, bool>>? predicate = field.ToLowerInvariant() switch");
                AppendLine("{");
                IncreaseIndent();
                foreach (var field in stringFields)
                {
                    AppendLine($"\"{field.JsonField.ToLowerInvariant()}\" => e => e.{field.PropertyPath}.Contains(searchValue),");
                }
                AppendLine("_ => null");
                DecreaseIndent();
                AppendLine("};");
                AppendLine();
                AppendLine("if (predicate is not null)");
                Block(() =>
                {
                    AppendLine("combined = combined is null");
                    AppendLine("    ? predicate");
                    AppendLine($"    : {Adapters}.GridPredicate.Or(combined, predicate);");
                });
            });
            AppendLine();
            AppendLine("return combined is null");
            AppendLine($"    ? throw new {Adapters}.GridFilterRejectedException(null, {Adapters}.GridFilterRejection.UnusableValue)");
            AppendLine("    : query.Where(combined);");
        });
    }

    /// <summary>
    ///     Whether a name is a filterable field, the refusal of one that is not, and the refusals of a clause.
    /// </summary>
    /// <remarks>
    ///     A name the entity holds under a sensitive name is refused as withheld, every other one as unknown, as the
    ///     bridge refuses them. A withheld name that is also a field's name on the wire (a <c>[GridField]</c> alias)
    ///     is the field: the alias names a different property.
    /// </remarks>
    private void RenderRefusalHelpers()
    {
        var filterable = _model.Fields
            .Where(f => f.Filterable)
            .Select(f => f.JsonField.ToLowerInvariant())
            .Distinct()
            .ToList();
        var withheld = _model.WithheldProperties
            .Select(p => p.ToLowerInvariant())
            .Where(p => !filterable.Contains(p))
            .Distinct()
            .ToList();

        AppendLine(filterable.Count == 0
            ? "private static bool IsFilterable(string field) => false;"
            : $"private static bool IsFilterable(string field) => field.ToLowerInvariant() is {string.Join(" or ", filterable.Select(f => $"\"{f}\""))};");
        AppendLine();

        if (withheld.Count == 0)
        {
            AppendLine($"private static Exception RejectedField(string field) => new {Adapters}.GridFieldRejectedException(field, {Adapters}.GridFieldRejection.Unknown);");
        }
        else
        {
            AppendLine("private static Exception RejectedField(string field) => field.ToLowerInvariant() switch");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"{string.Join(" or ", withheld.Select(w => $"\"{w}\""))} => new {Adapters}.GridFieldRejectedException(field, {Adapters}.GridFieldRejection.Withheld),");
            AppendLine($"_ => new {Adapters}.GridFieldRejectedException(field, {Adapters}.GridFieldRejection.Unknown),");
            DecreaseIndent();
            AppendLine("};");
        }

        AppendLine();
        AppendLine($"private static Exception Malformed() => new {Adapters}.GridFilterRejectedException(null, {Adapters}.GridFilterRejection.Malformed);");
        AppendLine();
        AppendLine($"private static Exception Unusable(string field) => new {Adapters}.GridFilterRejectedException(field, {Adapters}.GridFilterRejection.UnusableValue);");
        AppendLine();
        AppendLine($"private static Exception Unsupported(string field, string? op) => new {Adapters}.GridFilterRejectedException(field, {Adapters}.GridFilterRejection.UnsupportedOperator, op);");
        AppendLine();

        // The values of an in or a between: a JSON array or a CLR collection; a string is not a list of its characters.
        AppendLine("private static IReadOnlyList<object?>? Elements(object? value) => Unwrap(value) switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("string => null,");
        AppendLine("global::System.Text.Json.JsonElement { ValueKind: global::System.Text.Json.JsonValueKind.Array } json => json.EnumerateArray().Select(e => (object?)e).ToList(),");
        AppendLine("IEnumerable items => items.Cast<object?>().ToList(),");
        AppendLine("_ => null,");
        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        // One comparison, or one per value of an in or a between, read the way ApplyPrimeNGFilter reads them.
        AppendLine("private static int TermsOf(PrimeNGFilterMetadata filterMeta)");
        IncreaseIndent();
        AppendLine("=> filterMeta.MatchMode?.ToLowerInvariant() is \"in\" or \"between\" && Elements(filterMeta.Value) is { Count: > 0 } values");
        AppendLine("    ? values.Count");
        AppendLine("    : 1;");
        DecreaseIndent();
    }
}
