namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The one definition of the foreign key a <c>[Relation.*]</c> produces.
/// </summary>
/// <remarks>
///     Two places need the answer and they must agree: <c>RelationGraphBuilder</c>, which decides what
///     to emit, and <c>TraitPropertyResolver</c>, which tells the other features what will exist.
///     While only the first knew the rule, a DTO property named after a generated foreign key resolved
///     to nothing — the projection dropped the column in silence and the client received an empty
///     Guid. Divergent copies of one rule is the same shape that also produced the
///     <c>[Filter(IgnoreCase)]</c> and <c>FilterMapRegistry</c> defects.
/// </remarks>
internal static class RelationForeignKeyNaming
{
    /// <summary>
    ///     The foreign key property name: the explicit one, or the navigation name with <c>Id</c>.
    /// </summary>
    /// <param name="explicitForeignKey">The attribute's <c>ForeignKey</c>, when given.</param>
    /// <param name="navigationName">The attribute's <c>NavigationName</c>, when given.</param>
    /// <param name="targetTypeName">The related type's simple name, the default navigation name.</param>
    public static string Name(string? explicitForeignKey, string? navigationName, string targetTypeName)
        => explicitForeignKey ?? (navigationName ?? targetTypeName) + "Id";

    /// <summary>
    ///     The foreign key type: the target's key type, nullable when the relation is optional.
    /// </summary>
    public static string Type(string targetIdType, bool isRequired)
        => isRequired ? targetIdType : $"{targetIdType}?";
}
