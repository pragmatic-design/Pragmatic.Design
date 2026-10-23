using Pragmatic.Persistence.Sql;

namespace Pragmatic.Persistence.Workload;

/// <summary>
///     A price with a history: a system-versioned table on SQL Server (its period columns hidden, not mapped), Flashback on Oracle, the PricesHistory
///     table its triggers keep on PostgreSQL and SQLite ([Temporal], SqlTemporal.CreateSql), none on MySQL; the temporal tests' entity.
/// </summary>
[Entity]
[Temporal]
public sealed class Price : ISqlEntity
{
    public Guid Id { get; set; }

    public string Sku { get; set; } = "";

    public decimal Amount { get; set; }
}
