using System.Linq.Expressions;
using System.Reflection;

namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Adapter for PrimeNG DataTable filter/sort models.
/// </summary>
public static class PrimeNGAdapter
{
    // Cached string method infos extracted via typed delegate — no runtime GetMethod calls
    private static readonly MethodInfo StringContainsMethod;
    private static readonly MethodInfo StringStartsWithMethod;
    private static readonly MethodInfo StringEndsWithMethod;

    static PrimeNGAdapter()
    {
        Expression<Func<string, string, bool>> contains = (s, v) => s.Contains(v);
        StringContainsMethod = ((MethodCallExpression)contains.Body).Method;

        Expression<Func<string, string, bool>> startsWith = (s, v) => s.StartsWith(v);
        StringStartsWithMethod = ((MethodCallExpression)startsWith.Body).Method;

        Expression<Func<string, string, bool>> endsWith = (s, v) => s.EndsWith(v);
        StringEndsWithMethod = ((MethodCallExpression)endsWith.Body).Method;
    }

    /// <summary>
    ///     Converts PrimeNG LazyLoadEvent to QueryBuilder operations.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The query builder.</param>
    /// <param name="lazyEvent">The PrimeNG lazy load event.</param>
    /// <returns>The configured query builder.</returns>
    public static Builder.QueryBuilder<TEntity> FromPrimeNG<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        this Builder.QueryBuilder<TEntity> builder,
        PrimeNGLazyLoadEvent lazyEvent) where TEntity : class
    {
        // Apply filtering
        if (lazyEvent.Filters != null)
        {
            foreach (var (field, filterMeta) in lazyEvent.Filters)
            {
                if (filterMeta.Value == null)
                    continue;

                var filter = CreateFilterExpression<TEntity>(field, filterMeta);
                if (filter != null)
                    builder = builder.WithFilter(filter);
            }
        }

        // Apply global filter
        if (!string.IsNullOrEmpty(lazyEvent.GlobalFilter) && lazyEvent.GlobalFilterFields != null)
        {
            var globalFilter = CreateGlobalFilter<TEntity>(
                lazyEvent.GlobalFilter,
                lazyEvent.GlobalFilterFields);

            if (globalFilter != null)
                builder = builder.WithFilter(globalFilter);
        }

        // Apply sorting
        if (!string.IsNullOrEmpty(lazyEvent.SortField))
        {
            var sortExpr = CreateSortExpression<TEntity>(lazyEvent.SortField);
            if (sortExpr != null)
            {
                var descending = lazyEvent.SortOrder == -1;
                builder = builder.WithSorting(sortExpr, descending);
            }
        }
        else if (lazyEvent.MultiSortMeta != null)
        {
            foreach (var sortMeta in lazyEvent.MultiSortMeta)
            {
                var sortExpr = CreateSortExpression<TEntity>(sortMeta.Field);
                if (sortExpr != null)
                {
                    var descending = sortMeta.Order == -1;
                    builder = builder.WithSorting(sortExpr, descending);
                }
            }
        }

        // Apply paging
        if (lazyEvent.Rows > 0)
        {
            var page = (lazyEvent.First / lazyEvent.Rows) + 1;
            builder = builder.WithPaging(page, lazyEvent.Rows);
        }

        return builder;
    }

    private static Expression<Func<TEntity, bool>>? CreateFilterExpression<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        string field, PrimeNGFilterMetadata filterMeta)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        // The entity's declared fields first, then the names that must never be exposed: a client
        // supplies this name, so filtering on a column the entity did not publish would answer
        // whether a row holds a value — an oracle over data nobody offered.
        if (AdapterFieldPolicy.ResolveQueryableMember<TEntity>(parameter, field) is not { } property)
            return null;

        try
        {
            var value = filterMeta.Value;

            // Convert value to property type
            var convertedValue = ConvertValue(value, property.Type);
            var constant = Expression.Constant(convertedValue, property.Type);

            var matchMode = filterMeta.MatchMode?.ToLowerInvariant() ?? "contains";

            Expression body = matchMode switch
            {
                "equals" => Expression.Equal(property, constant),
                "notequals" => Expression.NotEqual(property, constant),
                "lt" => Expression.LessThan(property, constant),
                "lte" => Expression.LessThanOrEqual(property, constant),
                "gt" => Expression.GreaterThan(property, constant),
                "gte" => Expression.GreaterThanOrEqual(property, constant),
                "contains" => CreateStringContains(property, constant),
                "startswith" => CreateStringStartsWith(property, constant),
                "endswith" => CreateStringEndsWith(property, constant),
                "notcontains" => Expression.Not(CreateStringContains(property, constant)),
                "in" => CreateInExpression(property, value),
                "between" => CreateBetweenExpression(property, value),
                _ => Expression.Equal(property, constant)
            };

            return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        }
        catch
        {
            return null;
        }
    }

    private static Expression<Func<TEntity, bool>>? CreateGlobalFilter<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        string searchValue,
        IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
            return null;

        var parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression? combinedBody = null;

        foreach (var field in fields)
        {
            try
            {
                if (AdapterFieldPolicy.ResolveQueryableMember<TEntity>(parameter, field) is not { } property)
                    continue;

                // Only apply global filter to string properties
                if (property.Type != typeof(string))
                    continue;

                var constant = Expression.Constant(searchValue, typeof(string));
                var body = CreateStringContains(property, constant);

                combinedBody = combinedBody == null
                    ? body
                    : Expression.OrElse(combinedBody, body);
            }
            catch
            {
                // Skip invalid property
            }
        }

        return combinedBody == null
            ? null
            : Expression.Lambda<Func<TEntity, bool>>(combinedBody, parameter);
    }

    private static Expression CreateStringContains(MemberExpression property, ConstantExpression value)
        => Expression.Call(property, StringContainsMethod, value);

    private static Expression CreateStringStartsWith(MemberExpression property, ConstantExpression value)
        => Expression.Call(property, StringStartsWithMethod, value);

    private static Expression CreateStringEndsWith(MemberExpression property, ConstantExpression value)
        => Expression.Call(property, StringEndsWithMethod, value);

    private static Expression CreateInExpression(MemberExpression property, object? value)
    {
        // value should be an array or list
        if (value is not System.Collections.IEnumerable enumerable)
            return Expression.Constant(true);

        var values = enumerable.Cast<object>().ToList();
        if (values.Count == 0)
            return Expression.Constant(true);

        Expression? combined = null;
        foreach (var item in values)
        {
            var constant = Expression.Constant(ConvertValue(item, property.Type), property.Type);
            var eq = Expression.Equal(property, constant);
            combined = combined == null ? eq : Expression.OrElse(combined, eq);
        }

        return combined ?? Expression.Constant(true);
    }

    private static Expression CreateBetweenExpression(MemberExpression property, object? value)
    {
        // value should be an array with [min, max]
        if (value is not System.Collections.IEnumerable enumerable)
            return Expression.Constant(true);

        var values = enumerable.Cast<object>().ToList();
        if (values.Count != 2)
            return Expression.Constant(true);

        var minConstant = Expression.Constant(ConvertValue(values[0], property.Type), property.Type);
        var maxConstant = Expression.Constant(ConvertValue(values[1], property.Type), property.Type);

        var gte = Expression.GreaterThanOrEqual(property, minConstant);
        var lte = Expression.LessThanOrEqual(property, maxConstant);

        return Expression.AndAlso(gte, lte);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            // Convert.ChangeType fails for enums — parse by name or numeric value instead.
            if (underlyingType.IsEnum)
            {
                if (value is string s)
                    return Enum.Parse(underlyingType, s, ignoreCase: true);
                return Enum.ToObject(underlyingType, value);
            }

            return Convert.ChangeType(value, underlyingType);
        }
        catch
        {
            return value;
        }
    }

    private static Expression<Func<TEntity, object>>? CreateSortExpression<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(string? field)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        // Sorting is the quieter half of the same leak: it never returns the value and it ranks the
        // page by it, so it goes through the same guard.
        if (AdapterFieldPolicy.ResolveQueryableMember<TEntity>(parameter, field) is not { } property)
            return null;

        try
        {
            var convert = Expression.Convert(property, typeof(object));
            return Expression.Lambda<Func<TEntity, object>>(convert, parameter);
        }
        catch
        {
            return null;
        }
    }
}
