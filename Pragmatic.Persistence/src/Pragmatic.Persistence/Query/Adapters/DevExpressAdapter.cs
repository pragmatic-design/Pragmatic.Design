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
        // Every clause of the filter and every sort descriptor nests the query one level deeper, and past
        // GridFilterTooLargeException.MaxTerms the request is refused before the chain is built.
        var terms = 0;

        // Apply filtering
        if (loadOptions.Filter is { Count: > 0 })
        {
            var filter = ParseDevExpressFilter<TEntity>(loadOptions.Filter, ref terms);
            if (filter != null)
                builder = builder.WithFilter(filter);
        }

        // Apply sorting
        if (loadOptions.Sort != null)
        {
            terms += loadOptions.Sort.Count;
            if (terms > GridFilterTooLargeException.MaxTerms)
                throw new GridFilterTooLargeException(terms);

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
        IReadOnlyList<object?> filter, ref int terms)
    {
        if (filter.Count == 0)
            return null;

        var parameter = Expression.Parameter(typeof(TEntity), "e");

        // The top level as the nested groups are read: as CLR values, so that a string is read once, here, and
        // an unreadable one is an unusable value rather than an exception wherever it is next compared.
        IReadOnlyList<object?> elements = [.. filter.Select(FilterValue.Unwrap)];
        var body = ParseFilterExpression<TEntity>(elements, parameter, ref terms);

        if (body == null)
            return null;

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    /// <summary>
    ///     Recursively parses a DevExpress filter array into an expression tree.
    ///     All sub-expressions share the same <paramref name="parameter" /> to ensure
    ///     the resulting lambda has a single, consistent parameter binding.
    /// </summary>
    /// <remarks>
    ///     <paramref name="terms" /> counts the clauses across every group, and the filter is refused past
    ///     <see cref="GridFilterTooLargeException.MaxTerms" /> before a deeper chain is built: what reads the
    ///     expression walks it recursively, and a wide enough one overflows the stack.
    /// </remarks>
    private static Expression? ParseFilterExpression<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        IReadOnlyList<object?> filter, ParameterExpression parameter, ref int terms)
    {
        if (filter.Count == 0)
            return null;

        // Unary NOT: ["!", [subfilter]]
        if (filter.Count == 2 && filter[0]?.ToString() == "!")
        {
            var subFilter = ToFilterList(filter[1]);
            if (subFilter != null)
            {
                var inner = ParseFilterExpression<TEntity>(subFilter, parameter, ref terms);
                return inner != null ? Expression.Not(inner) : null;
            }

            return null;
        }

        // Simple filter: ["field", "operator", value]
        if (filter.Count == 3 && IsFieldName(filter[0]))
        {
            if (++terms > GridFilterTooLargeException.MaxTerms)
                throw new GridFilterTooLargeException(terms);

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

                    var sub = ParseFilterExpression<TEntity>(subList, parameter, ref terms);
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
            var value = FilterValue.ReadString(json);
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
            var coerced = FilterValue.Coerce(value, property.Type);
            var operand = QueryParameter.Of(coerced, property.Type);
            var opName = op.ToLowerInvariant();

            // A string match against null means nothing, and string.StartsWith(null) throws on every row it is
            // evaluated against. The generated adapter drops such a clause; so does this one.
            if (coerced is null && opName is "contains" or "startswith" or "endswith")
                return null;

            Expression body = opName switch
            {
                "=" or "==" => Expression.Equal(property, operand),
                "<>" or "!=" => Expression.NotEqual(property, operand),
                ">" => Expression.GreaterThan(property, operand),
                ">=" => Expression.GreaterThanOrEqual(property, operand),
                "<" => Expression.LessThan(property, operand),
                "<=" => Expression.LessThanOrEqual(property, operand),
                "contains" => CreateStringContains(property, operand),
                "startswith" => CreateStringStartsWith(property, operand),
                "endswith" => CreateStringEndsWith(property, operand),
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
        // A value the client sent that is not of the column's type: "abc" or [1,2] for a number, a number
        // past the column's range. Convert.ChangeType answers each with its own exception, and each escaped
        // as a 500 (#310); the clause is unusable, like an unknown operator.
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
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
                json.EnumerateArray().Select(e => (object?)FilterValue.Read(e)).ToList(),
            _ => null
        };
    }

    private static Expression CreateStringContains(MemberExpression property, Expression value)
        => Expression.Call(property, StringContainsMethod, value);

    private static Expression CreateStringStartsWith(MemberExpression property, Expression value)
        => Expression.Call(property, StringStartsWithMethod, value);

    private static Expression CreateStringEndsWith(MemberExpression property, Expression value)
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
