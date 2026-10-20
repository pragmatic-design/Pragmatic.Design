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
        // Every column filter, every value of an in, every column of the global filter and every sort adds to
        // the query, and past GridFilterTooLargeException.MaxTerms the request is refused before any is built.
        var terms = 0;

        // Apply filtering
        if (lazyEvent.Filters != null)
        {
            foreach (var (field, filterMeta) in lazyEvent.Filters)
            {
                if (filterMeta.Value == null)
                    continue;

                terms = Count(terms, TermsOf(filterMeta));
                var filter = CreateFilterExpression<TEntity>(field, filterMeta);
                if (filter != null)
                    builder = builder.WithFilter(filter);
            }
        }

        // Apply global filter
        if (!string.IsNullOrEmpty(lazyEvent.GlobalFilter) && lazyEvent.GlobalFilterFields != null)
        {
            terms = Count(terms, lazyEvent.GlobalFilterFields.Count);
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
            terms = Count(terms, lazyEvent.MultiSortMeta.Count);
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

    private static int Count(int terms, int more)
    {
        terms += more;
        return terms > GridFilterTooLargeException.MaxTerms ? throw new GridFilterTooLargeException(terms) : terms;
    }

    // One comparison, or one per value of an in or a between, read the way CreateInExpression reads them.
    private static int TermsOf(PrimeNGFilterMetadata filterMeta)
        => filterMeta.MatchMode?.ToLowerInvariant() is "in" or "between"
           && FilterValue.Elements(filterMeta.Value) is { Count: > 0 } values
            ? values.Count
            : 1;

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

        var matchMode = filterMeta.MatchMode?.ToLowerInvariant() ?? "contains";

        try
        {
            // The operand per match mode: an in or a between converts each of its values. Converting the whole
            // list as one value of the column's type failed for every list, so neither ever applied (#317).
            var body = matchMode switch
            {
                "in" => CreateInExpression(property, filterMeta.Value),
                "between" => CreateBetweenExpression(property, filterMeta.Value),
                _ => CreateComparison(property, matchMode, filterMeta.Value),
            };

            return body is null ? null : Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        }
        // A clause the adapter cannot use, dropped as every adapter drops one (what it should be is #318):
        // FormatException, InvalidCastException and OverflowException from a value not of the column's type,
        // ArgumentException from an enum name it does not have or a value the parameter refuses, and
        // InvalidOperationException from a comparison the column's type does not define.
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException
                                      or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Expression? CreateComparison(MemberExpression property, string matchMode, object? value)
    {
        var converted = FilterValue.Coerce(value, property.Type);

        // A string match against null means nothing, and string.StartsWith(null) throws on every row it is
        // evaluated against; the other adapters drop such a clause, and so does this one.
        if (converted is null && matchMode is "contains" or "startswith" or "endswith" or "notcontains")
            return null;

        var operand = QueryParameter.Of(converted, property.Type);
        return matchMode switch
        {
            "equals" => Expression.Equal(property, operand),
            "notequals" => Expression.NotEqual(property, operand),
            "lt" => Expression.LessThan(property, operand),
            "lte" => Expression.LessThanOrEqual(property, operand),
            "gt" => Expression.GreaterThan(property, operand),
            "gte" => Expression.GreaterThanOrEqual(property, operand),
            "contains" => CreateStringContains(property, operand),
            "startswith" => CreateStringStartsWith(property, operand),
            "endswith" => CreateStringEndsWith(property, operand),
            "notcontains" => Expression.Not(CreateStringContains(property, operand)),
            _ => Expression.Equal(property, operand)
        };
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
            if (AdapterFieldPolicy.ResolveQueryableMember<TEntity>(parameter, field) is not { } property)
                continue;

            // Only apply global filter to string properties
            if (property.Type != typeof(string))
                continue;

            var operand = QueryParameter.Of(searchValue);
            var body = CreateStringContains(property, operand);

            combinedBody = combinedBody == null
                ? body
                : Expression.OrElse(combinedBody, body);
        }

        return combinedBody == null
            ? null
            : Expression.Lambda<Func<TEntity, bool>>(combinedBody, parameter);
    }

    private static Expression CreateStringContains(MemberExpression property, Expression value)
        => Expression.Call(property, StringContainsMethod, value);

    private static Expression CreateStringStartsWith(MemberExpression property, Expression value)
        => Expression.Call(property, StringStartsWithMethod, value);

    private static Expression CreateStringEndsWith(MemberExpression property, Expression value)
        => Expression.Call(property, StringEndsWithMethod, value);

    // An in over no values, or over something that is not a list, selects nothing to filter on: dropped, as before.
    private static Expression? CreateInExpression(MemberExpression property, object? value)
    {
        if (FilterValue.Elements(value) is not { Count: > 0 } values)
            return null;

        Expression? combined = null;
        foreach (var item in values)
        {
            var operand = QueryParameter.Of(FilterValue.Coerce(item, property.Type), property.Type);
            var eq = Expression.Equal(property, operand);
            combined = combined == null ? eq : Expression.OrElse(combined, eq);
        }

        return combined;
    }

    // A between is [min, max]; anything else is a clause the adapter cannot use.
    private static Expression? CreateBetweenExpression(MemberExpression property, object? value)
    {
        if (FilterValue.Elements(value) is not { Count: 2 } values)
            return null;

        var minValue = QueryParameter.Of(FilterValue.Coerce(values[0], property.Type), property.Type);
        var maxValue = QueryParameter.Of(FilterValue.Coerce(values[1], property.Type), property.Type);

        var gte = Expression.GreaterThanOrEqual(property, minValue);
        var lte = Expression.LessThanOrEqual(property, maxValue);

        return Expression.AndAlso(gte, lte);
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

        var convert = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Func<TEntity, object>>(convert, parameter);
    }
}
