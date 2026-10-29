using System.Linq.Expressions;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Engines;

/// <summary>
///     The values a <c>BulkUpdateAsync</c>'s setters carry, in the order of their <c>SetProperty</c> calls: the generator compiled the update
///     (its fields, the values' slots) from the lambda where it is written, and the values come from running the delegate the caller passed,
///     so a value it captures is read as it is. A value computed on the row is part of the compiled update and records nothing.
/// </summary>
/// <typeparam name="TEntity">The entity.</typeparam>
internal sealed class BulkSetterValues<TEntity> : IBulkSetters<TEntity>
    where TEntity : class
{
    private readonly List<object?> _values = [];

    /// <summary>The values <paramref name="set" /> sets, in the order of its calls.</summary>
    public static object?[] Record(Action<IBulkSetters<TEntity>> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var recorder = new BulkSetterValues<TEntity>();
        set(recorder);
        return [.. recorder._values];
    }

    /// <inheritdoc />
    public IBulkSetters<TEntity> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property, TProperty value)
    {
        _values.Add(value);
        return this;
    }

    /// <inheritdoc />
    public IBulkSetters<TEntity> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property, Expression<Func<TEntity, TProperty>> value) => this;
}
