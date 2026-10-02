using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The one definition of the navigation property a <c>[Relation.*]</c> produces.
/// </summary>
/// <remarks>
///     The companion of <see cref="RelationForeignKeyNaming" />, and it exists for the same reason:
///     <c>RelationGraphBuilder</c> decides what to emit, <c>TraitPropertyResolver</c> has to predict
///     it for the features that run before the property exists. If only the first knew the rule,
///     a DTO could not flatten through a generated navigation — <c>WorkItem.Description</c> would fail
///     on its first segment — and a nested DTO list would resolve to nothing, so the branch would be
///     dropped in silence and the client would receive an empty array.
/// </remarks>
internal static class RelationNavigationNaming
{
    /// <summary>
    ///     The reference navigation a <c>ManyToOne</c> or <c>OneToOne</c> puts on the declaring side.
    /// </summary>
    public static string Reference(string? navigationName, string targetTypeName)
        => navigationName ?? targetTypeName;

    /// <summary>
    ///     The collection navigation a <c>OneToMany</c> or <c>ManyToMany</c> puts on the declaring side.
    /// </summary>
    public static string Collection(string? navigationName, string targetTypeName)
        => navigationName ?? StringHelper.Pluralize(targetTypeName);
}
