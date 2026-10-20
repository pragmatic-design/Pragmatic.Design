using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates optimized, compile-time grid adapter code.
/// </summary>
internal sealed partial class GridAdapterTemplate : CSharpTemplate
{
    private readonly GridAdapterModel _model;

    public GridAdapterTemplate(GridAdapterModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[GridAdapter] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "GridAdapter", _model.Namespace),
            ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        AddUsing("System.Linq.Expressions");
        AddUsing("Pragmatic.Persistence.Query.Adapters");
        AppendLine();

        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        Class(_model.TypeName, RenderClassBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClassBody()
    {
        // Generate DevExpress adapter
        if (_model.Framework.HasFlag(GridFrameworkFlags.DevExpress))
        {
            RenderDevExpressApply();
            AppendLine();
        }

        // Generate PrimeNG adapter
        if (_model.Framework.HasFlag(GridFrameworkFlags.PrimeNG))
        {
            RenderPrimeNGApply();
            AppendLine();
        }

        // Generate shared filter/sort helpers
        RenderFilterDispatcher();
        AppendLine();
        RenderSortDispatcher();
        AppendLine();

        // Generate per-field sort methods and filter dispatcher
        RenderSortExpressionMethod();

        // Generate helper methods
        RenderHelperMethods();
    }

    private void RenderDevExpressApply()
    {
        XmlSummary($"Applies DevExpress LoadOptions to the query with compile-time optimized filtering.");
        XmlParam("query", "The source query.");
        XmlParam("options", "The DevExpress load options from frontend.");
        XmlReturns("The filtered, sorted, and paged query.");
        AppendLine($"public static IQueryable<{_model.EntityTypeFullName}> Apply(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("DevExpressLoadOptions options)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("// Every clause and every sort nests the query one level deeper; past the bound the request");
            AppendLine("// is refused before the chain is built (GridFilterTooLargeException).");
            AppendLine("var terms = 0;");
            AppendLine();

            // Filter
            AppendLine("if (options.Filter != null && options.Filter.Count > 0)");
            Block(() =>
            {
                if (_model.SupportNestedFilters)
                {
                    AppendLine("query = ApplyNestedFilter(query, options.Filter, ref terms);");
                }
                else
                {
                    AppendLine("terms = CountedTerms(terms + 1);");
                    AppendLine("query = ApplySimpleFilter(query, options.Filter);");
                }
            });
            AppendLine();

            // Sort
            AppendLine("if (options.Sort != null)");
            Block(() =>
            {
                AppendLine("terms = CountedTerms(terms + options.Sort.Count);");
                AppendLine("var isFirst = true;");
                AppendLine("foreach (var sort in options.Sort)");
                Block(() =>
                {
                    AppendLine("if (string.IsNullOrEmpty(sort.Selector)) continue;");
                    AppendLine("// A column the adapter does not know is skipped and returns the query as it was: only a sort");
                    AppendLine("// that applied makes the next one a ThenBy, which needs an ordered query under it.");
                    AppendLine("var before = query;");
                    AppendLine("query = ApplySort(query, sort.Selector, sort.Desc, isFirst);");
                    AppendLine("if (!ReferenceEquals(query, before)) isFirst = false;");
                });
            });
            AppendLine();

            // Paging
            AppendLine("if (options.Skip.HasValue)");
            IncreaseIndent();
            AppendLine("query = query.Skip(options.Skip.Value);");
            DecreaseIndent();
            AppendLine();
            AppendLine("if (options.Take.HasValue)");
            IncreaseIndent();
            AppendLine("query = query.Take(options.Take.Value);");
            DecreaseIndent();
            AppendLine();

            AppendLine("return query;");
        });
    }

    private void RenderPrimeNGApply()
    {
        XmlSummary($"Applies PrimeNG LazyLoadEvent to the query with compile-time optimized filtering.");
        XmlParam("query", "The source query.");
        XmlParam("lazyEvent", "The PrimeNG lazy load event from frontend.");
        XmlReturns("The filtered, sorted, and paged query.");
        AppendLine($"public static IQueryable<{_model.EntityTypeFullName}> Apply(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("PrimeNGLazyLoadEvent lazyEvent)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("// Every column filter (each value of an in or a between), global-filter column and sort nests the");
            AppendLine("// query one level deeper. Counted before anything is built, so a request past the bound is refused");
            AppendLine("// as too wide whatever else is wrong with it (GridFilterTooLargeException).");
            AppendLine("var terms = 0;");
            AppendLine("if (lazyEvent.Filters != null)");
            Block(() =>
            {
                AppendLine("foreach (var filter in lazyEvent.Filters)");
                Block(() => AppendLine("if (filter.Value.Value != null) terms += TermsOf(filter.Value);"));
            });
            AppendLine("if (!string.IsNullOrEmpty(lazyEvent.GlobalFilter)) terms += lazyEvent.GlobalFilterFields?.Count ?? 0;");
            AppendLine("if (string.IsNullOrEmpty(lazyEvent.SortField) && lazyEvent.MultiSortMeta != null) terms += lazyEvent.MultiSortMeta.Count;");
            AppendLine("_ = CountedTerms(terms);");
            AppendLine();

            // Filters
            AppendLine("if (lazyEvent.Filters != null)");
            Block(() =>
            {
                AppendLine("foreach (var filter in lazyEvent.Filters)");
                Block(() =>
                {
                    AppendLine("if (filter.Value.Value == null) continue;");
                    AppendLine("query = ApplyPrimeNGFilter(query, filter.Key, filter.Value);");
                });
            });
            AppendLine();

            // Global filter: a search with no column to search is refused with the rest, not ignored.
            AppendLine("if (!string.IsNullOrEmpty(lazyEvent.GlobalFilter))");
            Block(() =>
            {
                AppendLine("query = ApplyGlobalFilter(query, lazyEvent.GlobalFilter, lazyEvent.GlobalFilterFields ?? global::System.Array.Empty<string>());");
            });
            AppendLine();

            // Sort
            AppendLine("if (!string.IsNullOrEmpty(lazyEvent.SortField))");
            Block(() =>
            {
                AppendLine("query = ApplySort(query, lazyEvent.SortField, lazyEvent.SortOrder == -1, true);");
            });
            AppendLine("else if (lazyEvent.MultiSortMeta != null)");
            Block(() =>
            {
                AppendLine("var isFirst = true;");
                AppendLine("foreach (var sort in lazyEvent.MultiSortMeta)");
                Block(() =>
                {
                    AppendLine("if (string.IsNullOrEmpty(sort.Field)) continue;");
                    AppendLine("// Only a sort that applied makes the next one a ThenBy (see the DevExpress Apply).");
                    AppendLine("var before = query;");
                    AppendLine("query = ApplySort(query, sort.Field, sort.Order == -1, isFirst);");
                    AppendLine("if (!ReferenceEquals(query, before)) isFirst = false;");
                });
            });
            AppendLine();

            // Paging
            AppendLine("if (lazyEvent.Rows > 0)");
            Block(() =>
            {
                AppendLine("query = query.Skip(lazyEvent.First).Take(lazyEvent.Rows);");
            });
            AppendLine();

            AppendLine("return query;");
        });
    }

    private void RenderFilterDispatcher()
    {
        if (_model.SupportNestedFilters)
        {
            RenderNestedFilterMethod();
            AppendLine();
        }

        RenderSimpleFilterMethod();
        AppendLine();
        RenderPrimeNGFilterMethod();
        AppendLine();
        RenderGlobalFilterMethod();
    }

    private void RenderExpressionCombiner()
    {
        // Expression combiner
        AppendLine($"private static Expression<Func<{_model.EntityTypeFullName}, bool>> CombineExpressions(");
        IncreaseIndent();
        AppendLine($"Expression<Func<{_model.EntityTypeFullName}, bool>> left,");
        AppendLine($"Expression<Func<{_model.EntityTypeFullName}, bool>> right,");
        AppendLine("string op)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine($"var parameter = Expression.Parameter(typeof({_model.EntityTypeFullName}), \"e\");");
            AppendLine("var leftBody = ReplaceParameter(left.Body, left.Parameters[0], parameter);");
            AppendLine("var rightBody = ReplaceParameter(right.Body, right.Parameters[0], parameter);");
            AppendLine();
            AppendLine("var combined = op == \"or\"");
            IncreaseIndent();
            AppendLine("? Expression.OrElse(leftBody, rightBody)");
            AppendLine(": Expression.AndAlso(leftBody, rightBody);");
            DecreaseIndent();
            AppendLine();
            AppendLine($"return Expression.Lambda<Func<{_model.EntityTypeFullName}, bool>>(combined, parameter);");
        });
        AppendLine();

        AppendLine("private static Expression ReplaceParameter(Expression expr, ParameterExpression oldParam, ParameterExpression newParam)");
        Block(() =>
        {
            AppendLine("return new ParameterReplacer(oldParam, newParam).Visit(expr);");
        });
        AppendLine();

        // ParameterReplacer helper class
        AppendLine("private sealed class ParameterReplacer : ExpressionVisitor");
        Block(() =>
        {
            AppendLine("private readonly ParameterExpression _oldParam;");
            AppendLine("private readonly ParameterExpression _newParam;");
            AppendLine();
            AppendLine("public ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)");
            Block(() =>
            {
                AppendLine("_oldParam = oldParam;");
                AppendLine("_newParam = newParam;");
            });
            AppendLine();
            AppendLine("protected override Expression VisitParameter(ParameterExpression node)");
            Block(() =>
            {
                AppendLine("return node == _oldParam ? _newParam : base.VisitParameter(node);");
            });
        });
    }

    private void RenderSortDispatcher()
    {
        AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplySort(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("string field,");
        AppendLine("bool descending,");
        AppendLine("bool isFirst)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("return field.ToLowerInvariant() switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var field in _model.Fields.Where(f => f.Sortable))
            {
                AppendLine($"\"{field.JsonField.ToLowerInvariant()}\" => ApplySort{field.PropertyName}(query, descending, isFirst),");
            }
            AppendLine("_ => query");
            DecreaseIndent();
            AppendLine("};");
        });
    }

    private void RenderSortExpressionMethod()
    {
        // Generate sort methods for each field
        foreach (var field in _model.Fields.Where(f => f.Sortable))
        {
            AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplySort{field.PropertyName}(");
            IncreaseIndent();
            AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
            AppendLine("bool descending,");
            AppendLine("bool isFirst)");
            DecreaseIndent();
            Block(() =>
            {
                AppendLine("if (isFirst)");
                Block(() =>
                {
                    AppendLine($"return descending");
                    IncreaseIndent();
                    AppendLine($"? query.OrderByDescending(e => e.{field.PropertyPath})");
                    AppendLine($": query.OrderBy(e => e.{field.PropertyPath});");
                    DecreaseIndent();
                });
                AppendLine("else");
                Block(() =>
                {
                    AppendLine($"return descending");
                    IncreaseIndent();
                    AppendLine($"? ((IOrderedQueryable<{_model.EntityTypeFullName}>)query).ThenByDescending(e => e.{field.PropertyPath})");
                    AppendLine($": ((IOrderedQueryable<{_model.EntityTypeFullName}>)query).ThenBy(e => e.{field.PropertyPath});");
                    DecreaseIndent();
                });
            });
            AppendLine();
        }

        // Generate the main CreateFieldFilter dispatcher: the field first, so neither the operator nor the value
        // says whether a withheld column exists.
        AppendLine($"private static Expression<Func<{_model.EntityTypeFullName}, bool>> CreateFieldFilter(string field, string? op, object? value)");
        Block(() =>
        {
            AppendLine("if (!IsFilterable(field))");
            IncreaseIndent();
            AppendLine("throw RejectedField(field);");
            DecreaseIndent();
            AppendLine();
            AppendLine("if (op is null)");
            IncreaseIndent();
            AppendLine("throw Unsupported(field, op);");
            DecreaseIndent();
            AppendLine();
            AppendLine("return field.ToLowerInvariant() switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var filterField in _model.Fields.Where(f => f.Filterable))
            {
                AppendLine($"\"{filterField.JsonField.ToLowerInvariant()}\" => Create{filterField.PropertyName}Filter(field, op, value),");
            }
            AppendLine("_ => throw RejectedField(field),");
            DecreaseIndent();
            AppendLine("};");
        });
        AppendLine();

        // Generate per-field filter creators
        foreach (var filterField in _model.Fields.Where(f => f.Filterable))
        {
            RenderFieldFilterMethod(filterField);
            AppendLine();
        }
    }

    private void RenderFieldFilterMethod(GridFieldModel field)
    {
        AppendLine($"private static Expression<Func<{_model.EntityTypeFullName}, bool>> Create{field.PropertyName}Filter(string field, string op, object? value)");
        Block(() =>
        {
            RenderAllowedOperatorsGuard(field);
            RenderNullComparison(field);

            switch (field.TypeCategory)
            {
                case PropertyTypeCategory.String:
                    RenderStringFilter(field);
                    break;
                case PropertyTypeCategory.Numeric:
                    RenderNumericFilter(field);
                    break;
                case PropertyTypeCategory.Boolean:
                    RenderBooleanFilter(field);
                    break;
                case PropertyTypeCategory.DateTime:
                    RenderDateTimeFilter(field);
                    break;
                case PropertyTypeCategory.Guid:
                    RenderGuidFilter(field);
                    break;
                case PropertyTypeCategory.Enum:
                    RenderEnumFilter(field);
                    break;
                default:
                    AppendLine("throw Unsupported(field, op);");
                    break;
            }
        });
    }

    /// <summary>
    ///     A clause against <c>null</c>: equality and inequality on a column that can hold it, as the runtime adapter
    ///     compares; anything else is a value the column cannot be compared with.
    /// </summary>
    /// <remarks>
    ///     The dispatcher dropped every clause with a null value, so <c>["city","&lt;&gt;",null]</c> answered with
    ///     the rows that have no city as well (#318).
    /// </remarks>
    private void RenderNullComparison(GridFieldModel field)
    {
        AppendLine("if (Unwrap(value) is null)");
        if (field.TypeCategory != PropertyTypeCategory.String && !field.IsNullable)
        {
            IncreaseIndent();
            AppendLine("throw Unusable(field);");
            DecreaseIndent();
            AppendLine();
            return;
        }

        IncreaseIndent();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == null,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != null,");
        AppendLine("_ => throw Unusable(field),");
        DecreaseIndent();
        AppendLine("};");
        DecreaseIndent();
        AppendLine();
    }

    private void RenderAllowedOperatorsGuard(GridFieldModel field)
    {
        // [GridField(AllowedOperators=...)] is enforced here because the per-type switch below
        // accepts every operator regardless. A dev who restricts a field to ["equals"] does so to
        // close the startswith/contains boolean-oracle, and a restriction that is not enforced is
        // silently ignored. Reject any operator outside the allow-list before the switch runs.
        if (field.AllowedOperators is not { Count: > 0 } allowed)
            return;

        var tokens = ExpandOperatorAliases(allowed);
        if (tokens.Count == 0)
            return;

        var condition = string.Join(" or ", tokens.Select(t => $"\"{t}\""));
        AppendLine($"if (op.ToLowerInvariant() is not ({condition}))");
        IncreaseIndent();
        AppendLine("throw Unsupported(field, op);");
        DecreaseIndent();
        AppendLine();
    }

    /// <summary>
    ///     Expands the developer-supplied operator names into the full set of lowercased tokens the
    ///     generated switch accepts (e.g. "equals" also covers "=" and "=="), so a restriction stays
    ///     robust to the client sending an equivalent alias. Unknown entries pass through as literals.
    /// </summary>
    private static IReadOnlyList<string> ExpandOperatorAliases(EquatableArray<string> allowed)
    {
        string[][] groups =
        [
            ["=", "==", "equals"],
            ["<>", "!=", "notequals"],
            ["contains"],
            ["notcontains"],
            ["startswith"],
            ["endswith"],
            [">", "gt", "greaterthan"],
            [">=", "gte", "greaterthanorequal"],
            ["<", "lt", "lessthan"],
            ["<=", "lte", "lessthanorequal"],
        ];

        var tokens = new List<string>();
        foreach (var raw in allowed)
        {
            var a = raw.ToLowerInvariant();
            var matched = false;
            foreach (var group in groups)
            {
                if (Array.IndexOf(group, a) < 0)
                    continue;

                matched = true;
                foreach (var token in group)
                    if (!tokens.Contains(token))
                        tokens.Add(token);
            }

            if (!matched && !tokens.Contains(a))
                tokens.Add(a);
        }

        return tokens;
    }

    /// <summary>
    ///     Turns what a grid sent into a CLR value: a <c>JsonElement</c> into the thing it holds,
    ///     anything else unchanged.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The load options arrive over HTTP, and <c>DevExpressLoadOptions.Filter</c> is
    ///     <c>IReadOnlyList&lt;object?&gt;</c> — which System.Text.Json fills with <c>JsonElement</c>.
    ///     Without this, every place the adapter asks "is this a string" or hands the value to
    ///     <c>Convert.ChangeType</c> would answer no, and the filter would be dropped in silence. The
    ///     runtime <c>DevExpressAdapter</c> does the same conversion; the generated one exists to
    ///     replace it.
    /// </remarks>
    private void RenderUnwrapHelper()
    {
        AppendLine("private static object? Unwrap(object? value)");
        Block(() =>
        {
            AppendLine("if (value is not global::System.Text.Json.JsonElement json) return value;");
            AppendLine();
            AppendLine("return json.ValueKind switch");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("// A string whose bytes are not UTF-8 stays the element: no column converts it, and a null it");
            AppendLine("// would have been read as made [\"city\",\"<>\",…] a clause on nothing (#318).");
            AppendLine("global::System.Text.Json.JsonValueKind.String => ReadString(json) ?? (object)json,");
            AppendLine("global::System.Text.Json.JsonValueKind.True => true,");
            AppendLine("global::System.Text.Json.JsonValueKind.False => false,");
            AppendLine("global::System.Text.Json.JsonValueKind.Null or global::System.Text.Json.JsonValueKind.Undefined => null,");
            AppendLine("// A number is read as long when it is one, so an int column converts without");
            AppendLine("// going through a double and losing the last digits of a big identifier.");
            AppendLine("global::System.Text.Json.JsonValueKind.Number => json.TryGetInt64(out var whole) ? whole : json.GetDouble(),");
            AppendLine("_ => value");
            DecreaseIndent();
            AppendLine("};");
        });
        AppendLine();

        // A JsonElement's text is validated only when it is read, and GetString throws on bytes that are not
        // UTF-8: an unreadable string is a value the adapter cannot use, not an exception out of it (#310).
        AppendLine("private static string? ReadString(global::System.Text.Json.JsonElement json)");
        Block(() =>
        {
            AppendLine("try");
            Block(() => AppendLine("return json.GetString();"));
            AppendLine("catch (InvalidOperationException)");
            Block(() => AppendLine("return null;"));
        });
        AppendLine();

        // The text a filter compares against, null when there is none. An object the client sent is its raw JSON,
        // which GetRawText transcodes and throws on in the same way, so it is read just as carefully. An array is
        // not a text, as the runtime adapter refuses it, and a string still an element here is one that could not
        // be read.
        AppendLine("private static string? Text(object? value)");
        Block(() =>
        {
            AppendLine("if (Unwrap(value) is not global::System.Text.Json.JsonElement json) return Unwrap(value)?.ToString();");
            AppendLine();
            AppendLine("if (json.ValueKind is global::System.Text.Json.JsonValueKind.String or global::System.Text.Json.JsonValueKind.Array) return null;");
            AppendLine();
            AppendLine("try");
            Block(() => AppendLine("return json.GetRawText();"));
            AppendLine("catch (InvalidOperationException)");
            Block(() => AppendLine("return null;"));
        });
    }

    private void RenderStringFilter(GridFieldModel field)
    {
        AppendLine("var strValue = Text(value);");
        AppendLine("if (strValue is null) throw Unusable(field);");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == strValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != strValue,");
        AppendLine($"\"contains\" => e => e.{field.PropertyPath} != null && e.{field.PropertyPath}.Contains(strValue),");
        AppendLine($"\"notcontains\" => e => e.{field.PropertyPath} == null || !e.{field.PropertyPath}.Contains(strValue),");
        AppendLine($"\"startswith\" => e => e.{field.PropertyPath} != null && e.{field.PropertyPath}.StartsWith(strValue),");
        AppendLine($"\"endswith\" => e => e.{field.PropertyPath} != null && e.{field.PropertyPath}.EndsWith(strValue),");
        AppendLine("_ => throw Unsupported(field, op),");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderNumericFilter(GridFieldModel field)
    {
        var type = field.IsNullable ? field.UnderlyingType : field.PropertyType;

        AppendLine($"if (!TryConvert<{type}>(value, out var typedValue)) throw Unusable(field);");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine($"\">\" or \"gt\" => e => e.{field.PropertyPath} > typedValue,");
        AppendLine($"\">=\" or \"gte\" => e => e.{field.PropertyPath} >= typedValue,");
        AppendLine($"\"<\" or \"lt\" => e => e.{field.PropertyPath} < typedValue,");
        AppendLine($"\"<=\" or \"lte\" => e => e.{field.PropertyPath} <= typedValue,");
        AppendLine("_ => throw Unsupported(field, op),");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderBooleanFilter(GridFieldModel field)
    {
        AppendLine("if (!TryConvert<bool>(value, out var typedValue)) throw Unusable(field);");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine("_ => throw Unsupported(field, op),");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderDateTimeFilter(GridFieldModel field)
    {
        var type = field.UnderlyingType;

        AppendLine($"if (!TryConvert<{type}>(value, out var typedValue)) throw Unusable(field);");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine($"\">\" or \"gt\" => e => e.{field.PropertyPath} > typedValue,");
        AppendLine($"\">=\" or \"gte\" => e => e.{field.PropertyPath} >= typedValue,");
        AppendLine($"\"<\" or \"lt\" => e => e.{field.PropertyPath} < typedValue,");
        AppendLine($"\"<=\" or \"lte\" => e => e.{field.PropertyPath} <= typedValue,");
        AppendLine("_ => throw Unsupported(field, op),");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderGuidFilter(GridFieldModel field)
    {
        AppendLine("if (!TryConvert<Guid>(value, out var typedValue)) throw Unusable(field);");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine("_ => throw Unsupported(field, op),");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderEnumFilter(GridFieldModel field)
    {
        // Use fully qualified name to avoid namespace issues
        var enumType = $"global::{field.UnderlyingTypeFullName}";

        AppendLine($"if (!TryConvertEnum<{enumType}>(value, out var typedValue)) throw Unusable(field);");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine("_ => throw Unsupported(field, op),");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderHelperMethods()
    {
        RenderUnwrapHelper();
        AppendLine();

        RenderRefusalHelpers();
        AppendLine();

        // The width bound (#310): a chain of joined comparisons or sorts is walked recursively by whatever
        // reads the query, and a wide enough one overflows the stack, which ends the process.
        AppendLine("private static int CountedTerms(int terms)");
        IncreaseIndent();
        AppendLine("=> terms > global::Pragmatic.Persistence.Query.Adapters.GridFilterTooLargeException.MaxTerms");
        AppendLine("    ? throw new global::Pragmatic.Persistence.Query.Adapters.GridFilterTooLargeException(terms)");
        AppendLine("    : terms;");
        DecreaseIndent();
        AppendLine();

        // TryConvert<T> helper
        AppendLine("private static bool TryConvert<T>(object? value, out T result)");
        Block(() =>
        {
            AppendLine("result = default!;");
            AppendLine("value = Unwrap(value);");
            AppendLine("if (value == null) return false;");
            AppendLine();
            AppendLine("try");
            Block(() =>
            {
                AppendLine("var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);");
                AppendLine("result = (T)Convert.ChangeType(value, targetType);");
                AppendLine("return true;");
            });
            AppendLine("catch");
            Block(() =>
            {
                AppendLine("return false;");
            });
        });
        AppendLine();

        // TryConvertEnum<T> helper
        AppendLine("private static bool TryConvertEnum<T>(object? value, out T result) where T : struct, Enum");
        Block(() =>
        {
            AppendLine("result = default;");
            AppendLine("value = Unwrap(value);");
            AppendLine("if (value == null) return false;");
            AppendLine();
            AppendLine("// Try parsing as string");
            AppendLine("if (value is string strValue)");
            Block(() =>
            {
                AppendLine("return Enum.TryParse<T>(strValue, ignoreCase: true, out result);");
            });
            AppendLine();
            AppendLine("// Try converting as integer");
            AppendLine("try");
            Block(() =>
            {
                AppendLine("var intValue = Convert.ToInt32(value);");
                AppendLine("result = (T)Enum.ToObject(typeof(T), intValue);");
                AppendLine("return Enum.IsDefined(typeof(T), result);");
            });
            AppendLine("catch");
            Block(() =>
            {
                AppendLine("return false;");
            });
        });
    }

    private AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "private" => AccessModifier.Private,
            "protected" => AccessModifier.Protected,
            _ => AccessModifier.Public
        };
    }
}
