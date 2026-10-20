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
        // the query, and past GridFilterTooLargeException.MaxTerms the request is refused before any is built:
        // counted first, so the width is refused as the width whatever else is wrong with the request.
        var columnFilters = lazyEvent.Filters?.Where(f => f.Value.Value != null).ToList() ?? [];
        var globalFields = string.IsNullOrEmpty(lazyEvent.GlobalFilter) ? null : lazyEvent.GlobalFilterFields ?? [];
        var sorts = string.IsNullOrEmpty(lazyEvent.SortField) ? lazyEvent.MultiSortMeta?.Count ?? 0 : 0;
        Count(columnFilters.Sum(f => TermsOf(f.Value)) + (globalFields?.Count ?? 0) + sorts);

        // Apply filtering
        foreach (var (field, filterMeta) in columnFilters)
            builder = builder.WithFilter(CreateFilterExpression<TEntity>(field, filterMeta));

        // Apply global filter
        // A search text with no column to search is refused with the rest (CreateGlobalFilter), not ignored.
        if (globalFields != null)
            builder = builder.WithFilter(CreateGlobalFilter<TEntity>(lazyEvent.GlobalFilter!, globalFields));

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

    private static void Count(int terms)
    {
        if (terms > GridFilterTooLargeException.MaxTerms)
            throw new GridFilterTooLargeException(terms);
    }

    // One comparison, or one per value of an in or a between, read the way CreateInExpression reads them.
    private static int TermsOf(PrimeNGFilterMetadata filterMeta)
        => filterMeta.MatchMode?.ToLowerInvariant() is "in" or "between"
           && FilterValue.Elements(filterMeta.Value) is { Count: > 0 } values
            ? values.Count
            : 1;

    private static Expression<Func<TEntity, bool>> CreateFilterExpression<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        string field, PrimeNGFilterMetadata filterMeta)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        // The entity's declared fields first, then the names that must never be exposed: a client
        // supplies this name, so filtering on a column the entity did not publish would answer
        // whether a row holds a value — an oracle over data nobody offered. Refused, not skipped: a
        // column filter left out answers with more rows than the grid shows as filtered (#318).
        var property = AdapterFieldPolicy.ResolveFilterableMember<TEntity>(parameter, field);
        var matchMode = filterMeta.MatchMode?.ToLowerInvariant() ?? "contains";

        // The operand per match mode: an in or a between converts each of its values. Converting the whole
        // list as one value of the column's type failed for every list, so neither ever applied (#317).
        var body = matchMode switch
        {
            "in" => CreateInExpression(property, field, filterMeta.Value),
            "between" => CreateBetweenExpression(property, field, filterMeta.Value),
            _ => CreateComparison(property, field, filterMeta.MatchMode, filterMeta.Value),
        };

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }

    private static Expression CreateComparison(MemberExpression property, string field, string? sentMode, object? value)
    {
        var matchMode = sentMode?.ToLowerInvariant() ?? "contains";

        // An unknown match mode was read as equals: dateBefore answered with the rows on that date.
        if (matchMode is not ("equals" or "notequals" or "lt" or "lte" or "gt" or "gte"
                              or "contains" or "startswith" or "endswith" or "notcontains"))
            throw new GridFilterRejectedException(field, GridFilterRejection.UnsupportedOperator, sentMode);

        // Against null only equality and inequality mean something: a string match throws on every row it is
        // evaluated against, string.StartsWith(null).
        if (FilterValue.Unwrap(value) is null && matchMode is not ("equals" or "notequals"))
            throw new GridFilterRejectedException(field, GridFilterRejection.UnusableValue);

        var operand = Parameter(value, property.Type, field);
        try
        {
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
                _ => Expression.Not(CreateStringContains(property, operand)),
            };
        }
        // A match mode the column's type does not define: a string match on a number is an ArgumentException, an
        // ordering on a type with no comparison an InvalidOperationException.
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            throw new GridFilterRejectedException(field, GridFilterRejection.UnsupportedOperator, sentMode);
        }
    }

    /// <summary>
    ///     The value as a parameter of the column's type, or the refusal of one the column cannot be compared with:
    ///     "abc" for a number, a number past its range, a name the enum does not have, null for a column that
    ///     cannot hold it.
    /// </summary>
    private static Expression Parameter(object? value, Type type, string field)
    {
        try
        {
            return QueryParameter.Of(FilterValue.Coerce(value, type), type);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new GridFilterRejectedException(field, GridFilterRejection.UnusableValue);
        }
    }

    /// <summary>The search box over the text columns it names, joined by OR.</summary>
    /// <remarks>
    ///     A column it names that the adapter does not filter on is refused, like a column filter; one that is not
    ///     text is passed over, since a grid lists every column it shows. One that names no text column at all
    ///     restricts nothing, and is refused rather than answered with every row (#318).
    /// </remarks>
    private static Expression<Func<TEntity, bool>> CreateGlobalFilter<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(
        string searchValue,
        IReadOnlyList<string> fields)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression? combinedBody = null;

        foreach (var field in fields)
        {
            var property = AdapterFieldPolicy.ResolveFilterableMember<TEntity>(parameter, field);
            if (property.Type != typeof(string))
                continue;

            var body = CreateStringContains(property, QueryParameter.Of(searchValue));
            combinedBody = combinedBody == null ? body : Expression.OrElse(combinedBody, body);
        }

        return combinedBody == null
            ? throw new GridFilterRejectedException(null, GridFilterRejection.UnusableValue)
            : Expression.Lambda<Func<TEntity, bool>>(combinedBody, parameter);
    }

    private static Expression CreateStringContains(MemberExpression property, Expression value)
        => Expression.Call(property, StringContainsMethod, value);

    private static Expression CreateStringStartsWith(MemberExpression property, Expression value)
        => Expression.Call(property, StringStartsWithMethod, value);

    private static Expression CreateStringEndsWith(MemberExpression property, Expression value)
        => Expression.Call(property, StringEndsWithMethod, value);

    // An in over no values, or over something that is not a list, selects nothing to filter on.
    private static Expression CreateInExpression(MemberExpression property, string field, object? value)
    {
        if (FilterValue.Elements(value) is not { Count: > 0 } values)
            throw new GridFilterRejectedException(field, GridFilterRejection.UnusableValue);

        Expression? combined = null;
        foreach (var item in values)
        {
            var eq = Expression.Equal(property, Parameter(item, property.Type, field));
            combined = combined == null ? eq : Expression.OrElse(combined, eq);
        }

        return combined!;
    }

    // A between is [min, max]; anything else is a clause the adapter cannot use.
    private static Expression CreateBetweenExpression(MemberExpression property, string field, object? value)
    {
        if (FilterValue.Elements(value) is not { Count: 2 } values)
            throw new GridFilterRejectedException(field, GridFilterRejection.UnusableValue);

        var minValue = Parameter(values[0], property.Type, field);
        var maxValue = Parameter(values[1], property.Type, field);

        try
        {
            return Expression.AndAlso(
                Expression.GreaterThanOrEqual(property, minValue),
                Expression.LessThanOrEqual(property, maxValue));
        }
        // A column whose type has no ordering: a between on it is a match mode it does not define.
        catch (InvalidOperationException)
        {
            throw new GridFilterRejectedException(field, GridFilterRejection.UnsupportedOperator, "between");
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

        var convert = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Func<TEntity, object>>(convert, parameter);
    }
}
