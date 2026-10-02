using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Lookups;

/// <summary>
///     A small reference table. <c>[Lookup]</c> tells the SG to generate an
///     <see cref="ILookupCache{T,TId}"/> implementation for this type and register a hosted
///     service (<c>LookupPreloadHostedService</c>) that loads all rows once at startup, so
///     navigation code can resolve a currency synchronously with no per-call DB query.
/// </summary>
[Entity]
[Lookup]
public partial class Currency : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    /// <summary>ISO 4217 code (e.g. "EUR").</summary>
    [LogicKey]
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Symbol { get; set; } = "";
}
