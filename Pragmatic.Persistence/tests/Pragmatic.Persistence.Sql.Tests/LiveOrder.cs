using System.Linq.Expressions;
using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The Orders table seen through a visibility rule: a deleted order is not there, for every read, unless
///     the chain says <c>WithoutFilters()</c>. A test-only entity on the workload's table, so the rule can be
///     checked against the seed without changing what the benchmarks measure.
/// </summary>
[Entity]
[Table("Orders")]
[VisibleWhen<NotDeleted>]
public sealed class LiveOrder
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Number { get; set; } = "";
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class NotDeleted : VisibilityRule<LiveOrder>
{
    public override Expression<Func<LiveOrder, bool>> ToExpression() => order => !order.IsDeleted;
}
