using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query;

/// <summary>
///     A value placed into a runtime-built expression tree as EF Core parameterizes it, not as a constant.
/// </summary>
/// <remarks>
///     <para>
///         EF Core inlines an <see cref="Expression.Constant(object?, Type)" /> into the SQL and into its
///         compiled-query cache key, so every distinct value a grid sends compiled a query of its own, took an
///         entry in that cache and a plan in the database's (#138). A member access on a captured closure is what
///         EF turns into a parameter, as it does for the lambdas the generator emits: one compiled query, whatever
///         the value.
///     </para>
///     <para>
///         No reflection: the closure is written as a lambda, and the conversion to the property's type is an
///         expression node.
///     </para>
/// </remarks>
internal static class QueryParameter
{
    /// <summary>The value as a parameter of <paramref name="type" />.</summary>
    /// <exception cref="ArgumentException">
    ///     The value is null for a non-nullable value type, or not of <paramref name="type" />: the refusals
    ///     <c>Expression.Constant(value, type)</c> made, kept here so a caller that dropped such a filter still
    ///     drops it instead of failing when the query runs.
    /// </exception>
    public static Expression Of(object? value, Type type)
    {
        _ = Expression.Constant(value, type);

        Expression<Func<object?>> captured = () => value;
        return Expression.Convert(captured.Body, type);
    }

    /// <summary>The value as a parameter of its own type.</summary>
    public static Expression Of<T>(T value)
    {
        Expression<Func<T>> captured = () => value;
        return captured.Body;
    }
}
