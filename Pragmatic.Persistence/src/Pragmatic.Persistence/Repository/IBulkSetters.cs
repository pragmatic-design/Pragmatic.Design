using System.Linq.Expressions;

namespace Pragmatic.Persistence.Repository;

/// <summary>The values a <see cref="IBulkRepository{TEntity}.BulkUpdateAsync" /> sets, one property each.</summary>
/// <typeparam name="TEntity">The entity.</typeparam>
/// <remarks>
///     Written as a chain on the lambda's parameter, as EF Core's <c>SetProperty</c> is.
/// </remarks>
public interface IBulkSetters<TEntity>
    where TEntity : class
{
    /// <summary>Sets <paramref name="property" /> to <paramref name="value" /> on every row the update selects.</summary>
    IBulkSetters<TEntity> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property, TProperty value);

    /// <summary>Sets <paramref name="property" /> to <paramref name="value" /> computed on each row: <c>SetProperty(p =&gt; p.Price, p =&gt; p.Price * 1.1m)</c>.</summary>
    IBulkSetters<TEntity> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property, Expression<Func<TEntity, TProperty>> value);
}
