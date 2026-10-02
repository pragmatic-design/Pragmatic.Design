using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Adapter for DevExpress DataGrid filter/sort models.
/// </summary>
public static class DevExpressAdapter
{
    // Cached string method infos extracted via typed delegate — no runtime GetMethod calls
    private static readonly MethodInfo StringContainsMethod;
    private static readonly MethodInfo StringStartsWithMethod;
    private static readonly MethodInfo StringEndsWithMethod;

    static DevExpressAdapter()
    {
        Expression<Func<string, string, bool>> contains = (s, v) => s.Contains(v);
        StringContainsMethod = ((MethodCallExpression)contains.Body).Method;

        Expression<Func<string, string, bool>> startsWith = (s, v) => s.StartsWith(v);
        StringStartsWithMethod = ((MethodCallExpression)startsWith.Body).Method;

        Expression<Func<string, string, bool>> endsWith = (s, v) => s.EndsWith(v);
        StringEndsWithMethod = ((MethodCallExpression)endsWith.Body).Method;
    }

    /// <summary>
    ///     Converts DevExpress LoadOptions to QueryBuilder operations.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The query builder.</param>
    /// <param name="loadOptions">The DevExpress load options.</param>
    /// <returns>The configured query builder.</returns>
    public static Builder.QueryBuilder<TEntity> FromDevExpress<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        this Builder.QueryBuilder<TEntity> builder,
        DevExpressLoadOptions loadOptions) where TEntity : class
    {
        // Apply filtering
        if (loadOptions.Filter is { Count: > 0 })
        {
            var filter = ParseDevExpressFilter<TEntity>(loadOptions.Filter);
            if (filter != null)
                builder = builder.WithFilter(filter);
        }

        // Apply sorting
        if (loadOptions.Sort != null)
        {
            foreach (var sort in loadOptions.Sort)
            {
                var sortExpr = CreateSortExpression<TEntity>(sort.Selector);
                if (sortExpr != null)
                {
                    builder = builder.WithSorting(sortExpr, sort.Desc);
                }
            }
        }

        // Apply paging
        if (loadOptions.Skip.HasValue || loadOptions.Take.HasValue)
        {
            var skip = loadOptions.Skip ?? 0;
            var take = loadOptions.Take ?? 20;
            // Guard: take==0 is a valid no-results request; treat as page 1 with pageSize=0
            var page = take > 0 ? (skip / take) + 1 : 1;
            builder = builder.WithPaging(page, take);
        }

        return builder;
    }

    private static Expression<Func<TEntity, bool>>? ParseDevExpressFilter<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        IReadOnlyList<object?> filter)
    {
        if (filter.Count == 0)
            return null;

        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var body = ParseFilterExpression<TEntity>(filter, parameter);

        if (body == null)
            return null;

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    /// <summary>
    ///     Recursively parses a DevExpress filter array into an expression tree.
    ///     All sub-expressions share the same <paramref name="parameter" /> to ensure
    ///     the resulting lambda has a single, consistent parameter binding.
    /// </summary>
    private static Expression? ParseFilterExpression<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        IReadOnlyList<object?> filter, ParameterExpression parameter)
    {
        if (filter.Count == 0)
            return null;

        // Unary NOT: ["!", [subfilter]]
        if (filter.Count == 2 && filter[0]?.ToString() == "!")
        {
            var subFilter = ToFilterList(filter[1]);
            if (subFilter != null)
            {
                var inner = ParseFilterExpression<TEntity>(subFilter, parameter);
                return inner != null ? Expression.Not(inner) : null;
            }

            return null;
        }

        // Simple filter: ["field", "operator", value]
        if (filter.Count == 3 && IsFieldName(filter[0]))
        {
            var field = filter[0]!.ToString()!;
            var op = filter[1]?.ToString();

            if (!string.IsNullOrEmpty(op))
                return CreateFilterBody<TEntity>(parameter, field, op, filter[2]);

            return null;
        }

        // Composite: [filter, "and"/"or", filter, "and"/"or", filter, ...]
        // Even indices = sub-filters, odd indices = connective operators
        if (filter.Count >= 3 && !IsFieldName(filter[0]))
        {
            var subExpressions = new List<Expression>();
            var connectives = new List<string>();

            for (var i = 0; i < filter.Count; i++)
            {
                if (i % 2 == 0)
                {
                    var subList = ToFilterList(filter[i]);
                    if (subList == null)
                        return null;

                    var sub = ParseFilterExpression<TEntity>(subList, parameter);
                    if (sub == null)
                        return null;

                    subExpressions.Add(sub);
                }
                else
                {
                    var connective = filter[i]?.ToString()?.ToLowerInvariant();
                    if (connective is not ("and" or "or"))
                        return null;

                    connectives.Add(connective);
                }
            }

            if (subExpressions.Count == 0)
                return null;

            var result = subExpressions[0];
            for (var i = 0; i < connectives.Count && i + 1 < subExpressions.Count; i++)
            {
                result = connectives[i] == "and"
                    ? Expression.AndAlso(result, subExpressions[i + 1])
                    : Expression.OrElse(result, subExpressions[i + 1]);
            }

            return result;
        }

        return null;
    }

    /// <summary>
    ///     Determines whether a filter element represents a field name (string, not a connective).
    ///     This distinguishes simple filters <c>["field", "op", value]</c> from composite filters
    ///     where <c>filter[0]</c> is itself an array.
    /// </summary>
    private static bool IsFieldName(object? element)
    {
        // A field name is a plain string that is not a connective keyword
        if (element is string s)
            return s is not ("and" or "or" or "!");

        // JsonElement string values are also field names
        if (element is JsonElement { ValueKind: JsonValueKind.String } json)
        {
            var value = json.GetString();
            return value is not (null or "and" or "or" or "!");
        }

        return false;
    }

    /// <summary>
    ///     Builds a comparison/string expression body for a single field filter,
    ///     reusing the shared <paramref name="parameter" />.
    /// </summary>
    private static Expression? CreateFilterBody<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        ParameterExpression parameter, string field, string op, object? value)
    {
        // The entity's declared fields first, then the names that must never be exposed — see
        // PrimeNGAdapter for why a caller-supplied field name is the case that needs both.
        if (AdapterFieldPolicy.ResolveQueryableMember<TEntity>(parameter, field) is not { } property)
            return null;

        try
        {

            // Coerce the value to the property type (handles JsonElement and type mismatches)
            var coerced = CoerceValue(value, property.Type);
            var constant = Expression.Constant(coerced, property.Type);

            Expression body = op.ToLowerInvariant() switch
            {
                "=" or "==" => Expression.Equal(property, constant),
                "<>" or "!=" => Expression.NotEqual(property, constant),
                ">" => Expression.GreaterThan(property, constant),
                ">=" => Expression.GreaterThanOrEqual(property, constant),
                "<" => Expression.LessThan(property, constant),
                "<=" => Expression.LessThanOrEqual(property, constant),
                "contains" => CreateStringContains(property, constant),
                "startswith" => CreateStringStartsWith(property, constant),
                "endswith" => CreateStringEndsWith(property, constant),
                _ => null!
            };

            return body;
        }
        catch (ArgumentException)  // Property not found
        {
            return null;
        }
        catch (InvalidOperationException) // Type conversion failure
        {
            return null;
        }
    }

    /// <summary>
    ///     Converts an object (possibly a <see cref="JsonElement" />) to the target type.
    /// </summary>
    private static object? CoerceValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        // Unwrap JsonElement to a CLR value
        if (value is JsonElement json)
            value = DeserializeJsonElement(json);

        if (value == null)
            return null;

        // Already the correct type
        if (targetType.IsInstanceOfType(value))
            return value;

        // Handle Nullable<T> — convert to the underlying type
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Enums need Enum.Parse for strings and Enum.ToObject for numeric values;
        // Convert.ChangeType throws InvalidCastException for both.
        if (underlyingType.IsEnum)
        {
            if (value is string s)
                return Enum.Parse(underlyingType, s, ignoreCase: true);
            return Enum.ToObject(underlyingType, value);
        }

        return Convert.ChangeType(value, underlyingType);
    }

    /// <summary>
    ///     Converts an object to <see cref="IReadOnlyList{T}" /> for recursive filter parsing.
    ///     Handles <see cref="JsonElement" /> arrays and CLR list types.
    /// </summary>
    private static IReadOnlyList<object?>? ToFilterList(object? value)
    {
        return value switch
        {
            IReadOnlyList<object?> list => list,
            IList<object?> list => list.ToArray(),
            JsonElement { ValueKind: JsonValueKind.Array } json =>
                json.EnumerateArray().Select(e => (object?)DeserializeJsonElement(e)).ToList(),
            _ => null
        };
    }

    /// <summary>
    ///     Converts a <see cref="JsonElement" /> to the most appropriate CLR type.
    /// </summary>
    private static object? DeserializeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(e => (object?)DeserializeJsonElement(e)).ToList() as object,
            _ => element.GetRawText()
        };
    }

    private static Expression CreateStringContains(MemberExpression property, ConstantExpression value)
        => Expression.Call(property, StringContainsMethod, value);

    private static Expression CreateStringStartsWith(MemberExpression property, ConstantExpression value)
        => Expression.Call(property, StringStartsWithMethod, value);

    private static Expression CreateStringEndsWith(MemberExpression property, ConstantExpression value)
        => Expression.Call(property, StringEndsWithMethod, value);

    private static Expression<Func<TEntity, object>>? CreateSortExpression<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(string? selector)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        if (AdapterFieldPolicy.ResolveQueryableMember<TEntity>(parameter, selector) is not { } property)
            return null;

        try
        {
            var convert = Expression.Convert(property, typeof(object));
            return Expression.Lambda<Func<TEntity, object>>(convert, parameter);
        }
        catch (ArgumentException)  // Property not found
        {
            return null;
        }
        catch (InvalidOperationException) // Type conversion failure
        {
            return null;
        }
    }
}
