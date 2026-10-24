using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The Orders table with the customer declared optional (a nullable foreign key), so the join kind the
///     generator picks can be checked: an optional navigation joins LEFT unless the predicate needs the joined
///     row; the workload's <see cref="Order" />, whose key is required, joins INNER. Translation only: no row of
///     the seed has a null customer.
/// </summary>
[Entity]
[Table("Orders")]
public sealed class OptionalCustomerOrder : ISqlEntity
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public string Number { get; set; } = "";
    public decimal Total { get; set; }

    public Customer? Customer { get; set; }
}
