namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     The [Relation.*] rule a <see cref="RelationDiagnosticModel" /> violates.
///     Mapped to its <c>PRAG06xx</c> descriptor at report time.
/// </summary>
internal enum RelationDiagnosticKind
{
    /// <summary>PRAG0612 — several relations target the same type and at least one is unnamed.</summary>
    AmbiguousRelation,

    /// <summary>PRAG0618 - a one-to-one whose principal end is not decided.</summary>
    OneToOnePrincipalNotDecided,

    /// <summary>PRAG0631 - [PartOf] on an entity with no relation to the parent.</summary>
    PartOfWithoutRelation,

    /// <summary>PRAG0632 - several relations to the parent and no Via.</summary>
    PartOfViaRequired,

    /// <summary>PRAG0633 - Via names no relation to the parent.</summary>
    PartOfViaNotFound,

    /// <summary>PRAG0634 - the ownership edge does not cascade.</summary>
    PartOfEdgeDoesNotCascade,

    /// <summary>PRAG0619 - a navigation or a foreign key written as a property.</summary>
    RelationWrittenByHand,

    /// <summary>PRAG0635 - an EF Core relational attribute on an entity property.</summary>
    EfCoreRelationAttribute,

    /// <summary>PRAG0610 - the two ends name the child's navigation differently.</summary>
    InverseContradictsTheDeclaredName,

    /// <summary>PRAG0611 - the delete behaviour written on the side that does not own it.</summary>
    DeleteBehaviourNotOwned,

    /// <summary>PRAG0617 — several relations to one type leave the inverse name to the convention.</summary>
    InverseNameNotDeclared,

    /// <summary>PRAG0613 — <c>Inverse</c> names a navigation that does not exist on the target.</summary>
    InversePropertyNotFound,

    /// <summary>PRAG0614 — the inverse navigation exists but does not point back to this entity.</summary>
    InversePropertyTypeMismatch,

    /// <summary>PRAG0615 — two relations produce a navigation with the same name.</summary>
    DuplicateNavigationName,

    /// <summary>PRAG0616 — a join entity whose foreign keys the declaration does not name.</summary>
    JoinEntityKeysNotNamed
}
