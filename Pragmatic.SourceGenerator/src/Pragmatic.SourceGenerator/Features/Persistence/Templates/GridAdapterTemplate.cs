using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates optimized, compile-time grid adapter code.
/// </summary>
internal sealed class GridAdapterTemplate : CSharpTemplate
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
            // Filter
            AppendLine("if (options.Filter != null && options.Filter.Count > 0)");
            Block(() =>
            {
                if (_model.SupportNestedFilters)
                {
                    AppendLine("query = ApplyNestedFilter(query, options.Filter);");
                }
                else
                {
                    AppendLine("query = ApplySimpleFilter(query, options.Filter);");
                }
            });
            AppendLine();

            // Sort
            AppendLine("if (options.Sort != null)");
            Block(() =>
            {
                AppendLine("var isFirst = true;");
                AppendLine("foreach (var sort in options.Sort)");
                Block(() =>
                {
                    AppendLine("if (string.IsNullOrEmpty(sort.Selector)) continue;");
                    AppendLine("query = ApplySort(query, sort.Selector, sort.Desc, isFirst);");
                    AppendLine("isFirst = false;");
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

            // Global filter
            AppendLine("if (!string.IsNullOrEmpty(lazyEvent.GlobalFilter) && lazyEvent.GlobalFilterFields != null)");
            Block(() =>
            {
                AppendLine("query = ApplyGlobalFilter(query, lazyEvent.GlobalFilter, lazyEvent.GlobalFilterFields);");
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
                    AppendLine("query = ApplySort(query, sort.Field, sort.Order == -1, isFirst);");
                    AppendLine("isFirst = false;");
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

    private void RenderNestedFilterMethod()
    {
        AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplyNestedFilter(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("IReadOnlyList<object?> filter)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("var expr = ParseFilterExpression(filter);");
            AppendLine("return expr != null ? query.Where(expr) : query;");
        });
        AppendLine();

        // Recursive filter expression parser
        AppendLine($"private static Expression<Func<{_model.EntityTypeFullName}, bool>>? ParseFilterExpression(IReadOnlyList<object?> filter)");
        Block(() =>
        {
            AppendLine("if (filter.Count == 0) return null;");
            AppendLine();
            AppendLine("// Simple filter: [field, op, value]");
            AppendLine("// ⚠️ Unwrapped first: a filter that arrived over HTTP is a list of JsonElement,");
            AppendLine("// not of string, so testing `is string` here matched nothing and the whole");
            AppendLine("// branch was dead — the query came back unfiltered.");
            AppendLine("if (filter.Count == 3 && Unwrap(filter[0]) is string field && Unwrap(filter[1]) is string op)");
            Block(() =>
            {
                AppendLine("return CreateFieldFilter(field, op, Unwrap(filter[2]));");
            });
            AppendLine();
            AppendLine("// Composite filter: [filter1, \"and\"/\"or\", filter2, ...]");
            AppendLine("if (filter.Count >= 3)");
            Block(() =>
            {
                AppendLine($"Expression<Func<{_model.EntityTypeFullName}, bool>>? combined = null;");
                AppendLine("var currentOp = \"and\";");
                AppendLine();
                AppendLine("for (var i = 0; i < filter.Count; i++)");
                Block(() =>
                {
                    AppendLine("if (Unwrap(filter[i]) is string logical && (logical == \"and\" || logical == \"or\"))");
                    Block(() =>
                    {
                        AppendLine("currentOp = logical;");
                        AppendLine("continue;");
                    });
                    AppendLine();
                    AppendLine("// A nested group that arrived as JSON is an array element, not a list.");
                    AppendLine("if (filter[i] is global::System.Text.Json.JsonElement { ValueKind: global::System.Text.Json.JsonValueKind.Array } jsonGroup)");
                    Block(() =>
                    {
                        AppendLine("var groupExpr = ParseFilterExpression([.. jsonGroup.EnumerateArray().Select(e => (object?)e)]);");
                        AppendLine("if (groupExpr != null)");
                        Block(() =>
                        {
                            AppendLine("combined = combined == null ? groupExpr : CombineExpressions(combined, groupExpr, currentOp);");
                        });
                        AppendLine("continue;");
                    });
                    AppendLine();
                    AppendLine("if (filter[i] is IReadOnlyList<object?> nested)");
                    Block(() =>
                    {
                        AppendLine("var nestedExpr = ParseFilterExpression(nested);");
                        AppendLine("if (nestedExpr != null)");
                        Block(() =>
                        {
                            AppendLine("combined = combined == null ? nestedExpr : CombineExpressions(combined, nestedExpr, currentOp);");
                        });
                    });
                    AppendLine("else if (filter[i] is IList list)");
                    Block(() =>
                    {
                        AppendLine("var nestedList = list.Cast<object?>().ToList();");
                        AppendLine("var nestedExpr = ParseFilterExpression(nestedList);");
                        AppendLine("if (nestedExpr != null)");
                        Block(() =>
                        {
                            AppendLine("combined = combined == null ? nestedExpr : CombineExpressions(combined, nestedExpr, currentOp);");
                        });
                    });
                });
                AppendLine("return combined;");
            });
            AppendLine();
            AppendLine("return null;");
        });
        AppendLine();

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

    private void RenderSimpleFilterMethod()
    {
        AppendLine($"private static IQueryable<{_model.EntityTypeFullName}> ApplySimpleFilter(");
        IncreaseIndent();
        AppendLine($"IQueryable<{_model.EntityTypeFullName}> query,");
        AppendLine("IReadOnlyList<object?> filter)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("if (filter.Count != 3) return query;");
            AppendLine("var field = filter[0]?.ToString();");
            AppendLine("var op = filter[1]?.ToString();");
            AppendLine("if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(op)) return query;");
            AppendLine();
            AppendLine("var expr = CreateFieldFilter(field, op, filter[2]);");
            AppendLine("return expr != null ? query.Where(expr) : query;");
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
            AppendLine("var matchMode = filterMeta.MatchMode?.ToLowerInvariant() ?? \"contains\";");
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
            AppendLine("_ => \"=\"");
            DecreaseIndent();
            AppendLine("};");
            AppendLine();
            AppendLine("var expr = CreateFieldFilter(field, op, filterMeta.Value);");
            AppendLine("return expr != null ? query.Where(expr) : query;");
        });
    }

    /// <summary>
    ///     The grid's single search box: one value matched against every text field, joined by OR.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A typed lambda, not an expression built by hand — <c>typeof(string).GetMethod("Contains")</c>
    ///     and <c>Expression.Property(parameter, "Name")</c> would be reflection, at run time, in the
    ///     consumer's application, emitted by an attribute whose own documentation promises "compile-time
    ///     optimized, strongly-typed LINQ expressions". The switch knows the field at compile time, so a
    ///     typed lambda says the same thing with none of it. <c>DevExpressAdapter</c> in the runtime
    ///     captures its MethodInfo from a typed lambda the same way.
    ///     <para>
    ///         And nothing would catch it: the reflection ratchet skips <c>.g.cs</c> because "the
    ///         generator is measured by what it emits", then skips generator projects because "a
    ///         generator runs at compile time where reflection costs nothing" — so what a template puts
    ///         into an application is measured by neither half.
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
            if (stringFields.Count == 0)
            {
                AppendLine("return query;");
                return;
            }

            AppendLine($"global::System.Linq.Expressions.Expression<Func<{_model.EntityTypeFullName}, bool>>? combined = null;");
            AppendLine();
            AppendLine("foreach (var field in fields)");
            Block(() =>
            {
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
                    AppendLine("    : global::Pragmatic.Persistence.Query.Adapters.GridPredicate.Or(combined, predicate);");
                });
            });
            AppendLine();
            AppendLine("return combined is null ? query : query.Where(combined);");
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

        // Generate the main CreateFieldFilter dispatcher
        AppendLine($"private static Expression<Func<{_model.EntityTypeFullName}, bool>>? CreateFieldFilter(string field, string op, object? value)");
        Block(() =>
        {
            AppendLine("if (value == null) return null;");
            AppendLine();
            AppendLine("return field.ToLowerInvariant() switch");
            AppendLine("{");
            IncreaseIndent();
            foreach (var filterField in _model.Fields.Where(f => f.Filterable))
            {
                AppendLine($"\"{filterField.JsonField.ToLowerInvariant()}\" => Create{filterField.PropertyName}Filter(op, value),");
            }
            AppendLine("_ => null");
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
        AppendLine($"private static Expression<Func<{_model.EntityTypeFullName}, bool>>? Create{field.PropertyName}Filter(string op, object? value)");
        Block(() =>
        {
            RenderAllowedOperatorsGuard(field);

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
                    AppendLine("return null;");
                    break;
            }
        });
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
        AppendLine($"if (op.ToLowerInvariant() is not ({condition})) return null;");
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
            AppendLine("global::System.Text.Json.JsonValueKind.String => json.GetString(),");
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
    }

    private void RenderStringFilter(GridFieldModel field)
    {
        AppendLine("var strValue = value?.ToString();");
        AppendLine("if (string.IsNullOrEmpty(strValue)) return null;");
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
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderNumericFilter(GridFieldModel field)
    {
        var type = field.IsNullable ? field.UnderlyingType : field.PropertyType;

        AppendLine($"if (!TryConvert<{type}>(value, out var typedValue)) return null;");
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
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderBooleanFilter(GridFieldModel field)
    {
        AppendLine("if (!TryConvert<bool>(value, out var typedValue)) return null;");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderDateTimeFilter(GridFieldModel field)
    {
        var type = field.UnderlyingType;

        AppendLine($"if (!TryConvert<{type}>(value, out var typedValue)) return null;");
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
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderGuidFilter(GridFieldModel field)
    {
        AppendLine("if (!TryConvert<Guid>(value, out var typedValue)) return null;");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderEnumFilter(GridFieldModel field)
    {
        // Use fully qualified name to avoid namespace issues
        var enumType = $"global::{field.UnderlyingTypeFullName}";

        AppendLine($"if (!TryConvertEnum<{enumType}>(value, out var typedValue)) return null;");
        AppendLine();
        AppendLine("return op.ToLowerInvariant() switch");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"\"=\" or \"==\" or \"equals\" => e => e.{field.PropertyPath} == typedValue,");
        AppendLine($"\"<>\" or \"!=\" or \"notequals\" => e => e.{field.PropertyPath} != typedValue,");
        AppendLine("_ => null");
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderHelperMethods()
    {
        RenderUnwrapHelper();
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
