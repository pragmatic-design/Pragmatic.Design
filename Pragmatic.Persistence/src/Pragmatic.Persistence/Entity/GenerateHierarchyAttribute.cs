namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Opt-in: generates recursive-CTE query methods — <c>GetDescendantsBy{Navigation}</c> and
///     <c>GetAncestorsBy{Navigation}</c> — over a self-referencing relation declared on the same entity.
/// </summary>
/// <remarks>
///     <para>
///         The attribute declares no relation of its own: it leans on a
///         <c>[Relation.ManyToOne&lt;TSelf&gt;]</c> already declared on the entity and takes the parent key
///         from it. With one self-referencing relation nothing more is needed; with several,
///         <see cref="Via" /> names the navigation that is the tree — the others are edges of a graph,
///         not a hierarchy.
///     </para>
///     <para>
///         The tree's edge has to be optional (<c>Required = false</c>): a root has no parent. An entity
///         may carry several trees, one attribute each, and the generated methods carry the navigation
///         name so that two trees never compete for one.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class GenerateHierarchyAttribute : Attribute
{
    /// <summary>
    ///     The navigation of the self-referencing relation that is this tree. Required when the entity
    ///     declares more than one relation to itself; otherwise the only one is taken.
    /// </summary>
    public string? Via { get; set; }
}
