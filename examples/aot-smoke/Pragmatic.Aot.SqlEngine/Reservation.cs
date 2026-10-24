using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A stay at a property: soft deleted, owned.</summary>
[Entity]
[SoftDelete]
[HasOwner]
[Relation.ManyToOne<Property>]
public partial class Reservation
{
    public DateTimeOffset CheckIn { get; set; }
}
