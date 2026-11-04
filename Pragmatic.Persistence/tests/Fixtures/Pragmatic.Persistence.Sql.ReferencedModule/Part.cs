using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.ReferencedModule;

/// <summary>A part, with its supplier: the foreign key (<c>SupplierId</c>) and the navigation are the shape's, generated in this assembly.</summary>
[Entity]
[Relation.ManyToOne<Supplier>]
public partial class Part : IEntity
{
    public string Code { get; private set; } = "";

    /// <summary>A computed member: not a column, from this assembly or another.</summary>
    public string Label => Code + "/" + Code.Length;

    /// <summary>Written by this module only.</summary>
    public void Describe(string code, Guid supplier)
    {
        Code = code;
        SetSupplierId(supplier);
    }
}
