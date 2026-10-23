namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A member of a Pragmatic entity's shape, as Pragmatic's templates declare it.</summary>
internal enum ShapeMemberKind
{
    /// <summary><c>[Entity]</c>'s key, assigned at construction.</summary>
    PersistenceId,

    /// <summary><c>Id</c>, the key's alias.</summary>
    Id,

    IsDeleted,
    DeletedAt,
    DeletedBy,
    CreatedAt,
    CreatedBy,
    UpdatedAt,
    UpdatedBy,

    /// <summary><c>[HasOwner]</c>: the owner and how it is assigned.</summary>
    Owner,

    /// <summary><c>[HasAccessScopes]</c>: the scopes, granted and revoked.</summary>
    AccessScopes,

    /// <summary><c>[ConcurrencyAware]</c>'s row version.</summary>
    RowVersion,

    /// <summary>A relation's foreign key (<see cref="ShapeMemberModel.Type" />: a Guid, nullable when optional).</summary>
    ForeignKey,

    /// <summary>The foreign key's setter.</summary>
    ForeignKeySetter,

    /// <summary>A reference navigation (<see cref="ShapeMemberModel.Required" />: never null).</summary>
    Reference,

    /// <summary>A collection navigation, a many-to-many one carrying its join table.</summary>
    Collection,

    /// <summary><c>IChangeTracking</c>'s members.</summary>
    ChangeTracking,

    /// <summary>A tracked property's setter (<see cref="ShapeMemberModel.Required" />: it records the change, on a root).</summary>
    TrackedSetter,
}
