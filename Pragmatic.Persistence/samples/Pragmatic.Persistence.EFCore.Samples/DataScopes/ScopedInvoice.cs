using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.DataScopes;

/// <summary>
///     A scoped entity used by the L3 data-scope demo. Implemented by hand (like the L1/L2
///     DataOwnership samples) because the full row-level-security pipeline is wired at host level.
///     <see cref="AccessScopes"/> is the JSON-array column the runtime materializes scope rules into.
/// </summary>
public sealed class ScopedInvoice : IScopedEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Number { get; init; }

    public required string Currency { get; init; }

    public decimal Amount { get; init; }

    /// <summary>Materialized scope tokens (e.g. "scope:eur-invoices").</summary>
    public List<string> AccessScopes { get; } = [];
}
