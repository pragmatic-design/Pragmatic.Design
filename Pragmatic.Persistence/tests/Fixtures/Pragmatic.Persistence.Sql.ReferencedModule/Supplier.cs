using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.ReferencedModule;

/// <summary>A supplier of parts: an entity of another module, which the suite reads and never writes, as a boundary reads through <c>[ReadAccess&lt;T&gt;]</c>.</summary>
[Entity]
[VisibleWhen<ListedOnly>]
public partial class Supplier : IEntity
{
    /// <summary>A private setter: from another assembly, the property has no setter Roslyn can see.</summary>
    public string Name { get; private set; } = "";

    /// <summary>Written through the setter Pragmatic generates (<c>SetStatus</c>), in this module only.</summary>
    public string Status { get; private set; } = "LISTED";

    /// <summary>Written by this module only.</summary>
    public void Rename(string name) => Name = name;
}
