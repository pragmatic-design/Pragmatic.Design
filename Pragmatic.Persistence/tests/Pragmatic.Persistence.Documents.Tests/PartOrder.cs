using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.ReferencedModule;

namespace PragmaticDocuments.Tests;

/// <summary>An order of this module to a supplier of another (<c>Supplier</c>, metadata here), as the Showcase's reservation names a catalogue's property.</summary>
[Entity]
[Relation.ManyToOne<Supplier>]
public partial class PartOrder : IEntity
{
    public string Reference { get; set; } = "";

    public int Quantity { get; set; }
}
