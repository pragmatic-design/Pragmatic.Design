using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     <see cref="IBulkSetters{TEntity}" /> over EF Core's <c>UpdateSettersBuilder</c>: each <c>SetProperty</c> is EF Core's, typed, so
///     <c>ExecuteUpdate</c> translates the update as if it had been written against EF Core.
/// </summary>
/// <typeparam name="TEntity">The entity.</typeparam>
public sealed class EfBulkSetters<TEntity>(UpdateSettersBuilder<TEntity> builder) : IBulkSetters<TEntity>
    where TEntity : class
{
    /// <inheritdoc />
    public IBulkSetters<TEntity> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property, TProperty value)
    {
        builder.SetProperty(property, value);
        return this;
    }

    /// <inheritdoc />
    public IBulkSetters<TEntity> SetProperty<TProperty>(Expression<Func<TEntity, TProperty>> property, Expression<Func<TEntity, TProperty>> value)
    {
        builder.SetProperty(property, value);
        return this;
    }
}
