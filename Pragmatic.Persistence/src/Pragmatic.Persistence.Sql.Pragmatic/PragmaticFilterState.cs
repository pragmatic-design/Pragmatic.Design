namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>What one filter does in one execution: kept, lifted, or the entity closed (no row).</summary>
internal enum PragmaticFilterState : byte
{
    Active,
    Lifted,
    Closed,
}
