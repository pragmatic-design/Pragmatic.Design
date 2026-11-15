using Pragmatic.Persistence.Entity;

namespace PragmaticEntities.Tests;

// The entities of ASoftDeleteHonoursOnSoftDeleteTests (#752): a shelf and the rows that point at it, one relation per OnSoftDelete. Embedded
// beside Entities.cs, so their tables are in the schema DialectSchemas migrates on the five databases.

/// <summary>A shelf, soft-deleted: the principal every relation below points at.</summary>
[Entity]
[SoftDelete]
public partial class OsdShelf : IEntity
{
    public string Name { get; set; } = "";
}

/// <summary>Holds its shelf: <c>OnSoftDelete = Restrict</c>.</summary>
[Entity]
[SoftDelete]
[Relation.ManyToOne<OsdShelf>.WithNavigation("Shelf", OnSoftDelete = DeleteBehavior.Restrict)]
public partial class OsdBook : IEntity
{
    public string Title { get; set; } = "";
}

/// <summary>Declares no <c>OnSoftDelete</c>: the shelf's soft delete leaves it as it is.</summary>
[Entity]
[Relation.ManyToOne<OsdShelf>.WithNavigation("Shelf")]
public partial class OsdPoster : IEntity
{
    public string Caption { get; set; } = "";
}

/// <summary>Lets go of its shelf: <c>OnSoftDelete = SetNull</c>, on an optional key.</summary>
[Entity]
[Relation.ManyToOne<OsdShelf>.WithNavigation("Shelf", Required = false, OnSoftDelete = DeleteBehavior.SetNull)]
public partial class OsdLabel : IEntity
{
    public string Text { get; set; } = "";
}

/// <summary>Goes with its shelf: <c>OnSoftDelete = Cascade</c>, and takes its own sheets with it.</summary>
[Entity]
[SoftDelete]
[Relation.ManyToOne<OsdShelf>.WithNavigation("Shelf", OnSoftDelete = DeleteBehavior.Cascade)]
public partial class OsdBinder : IEntity
{
    public string Title { get; set; } = "";
}

/// <summary>Goes with its binder: <c>OnSoftDelete = Cascade</c> one level further.</summary>
[Entity]
[SoftDelete]
[Relation.ManyToOne<OsdBinder>.WithNavigation("Binder", OnSoftDelete = DeleteBehavior.Cascade)]
public partial class OsdSheet : IEntity
{
    public string Text { get; set; } = "";
}
