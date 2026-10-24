using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>An amenity, versioned: two writers of one row meet a concurrency error.</summary>
[Entity]
[ConcurrencyAware]
public partial class Amenity : IEntity
{
    public string Name { get; set; } = "";
}
