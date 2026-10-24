using Pragmatic.Persistence.Entity;

namespace PragmaticDocumentSamples.Entities;

/// <summary>An amenity, versioned: its RowVersion is the document's _v, a stale write refused.</summary>
[Entity]
[ConcurrencyAware]
public partial class Amenity : IEntity
{
    public string Name { get; set; } = "";
}
