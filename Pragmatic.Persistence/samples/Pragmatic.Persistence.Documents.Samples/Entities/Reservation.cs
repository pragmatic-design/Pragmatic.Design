using Pragmatic.Persistence.Entity;

namespace PragmaticDocumentSamples.Entities;

/// <summary>A stay at a property: soft deleted, owned; the relation kept as its key (PropertyId).</summary>
[Entity]
[SoftDelete]
[HasOwner]
[Relation.ManyToOne<Property>]
public partial class Reservation : IEntity
{
    public int Nights { get; set; }
}
