namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>What kept a <c>[GenerateHierarchy]</c> from resolving to a tree.</summary>
internal enum HierarchyProblemKind
{
    /// <summary>The entity declares no relation to itself.</summary>
    NoSelfRelation,

    /// <summary>Several self-referencing relations, and <c>Via</c> does not say which is the tree.</summary>
    ViaRequired,

    /// <summary>The <c>Via</c> named matches no self-referencing navigation.</summary>
    ViaNotFound,

    /// <summary>The edge is required, so no row could be a root.</summary>
    EdgeRequired
}
