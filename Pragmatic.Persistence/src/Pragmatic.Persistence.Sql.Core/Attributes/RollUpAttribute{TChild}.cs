namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A stored aggregate the parent keeps over its <typeparamref name="TChild" /> rows (the repository's <c>[RollUp&lt;TChild&gt;]</c>, read by name): every
///     row write of a child (insert, update, upsert, save, delete, a batch, a graph save with its orphans, a graph delete) moves the parent's column
///     in the same transaction, by delta read from the child's table (its rows' contribution subtracted before the write, added after it), so
///     concurrent writers add up. The child's foreign key is <c>{Parent}Id</c>. The column is the database's: the parent's own writes leave it out
///     and an insert reads it back, as <see cref="DatabaseGeneratedAttribute" /> (its DDL gives it a default, 0). A set-based command on the child
///     (ExecuteUpdate, ExecuteDelete, ExecuteInsert, SyncAsync) would bypass it: refused at build time (PRAG3001).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RollUpAttribute<TChild>(string childProperty, RollUpAggregation aggregation = RollUpAggregation.Sum) : Attribute
    where TChild : class
{
    public string ChildProperty { get; } = childProperty;

    public RollUpAggregation Aggregation { get; } = aggregation;
}
