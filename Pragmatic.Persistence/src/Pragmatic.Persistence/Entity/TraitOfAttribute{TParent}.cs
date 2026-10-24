namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks a trait's child entity (a comment, a tag link, a note, an attachment) as the child of <typeparamref name="TParent" />:
///     visible only where its parent is, the parent's owner and access scopes applied through the navigation to it.
/// </summary>
/// <remarks>
///     <para>
///         Emitted by the generator on the children <c>[HasComments]</c>, <c>[HasTags]</c>, <c>[HasNotes]</c> and
///         <c>[HasAttachments]</c> produce; not meant to be written by hand.
///     </para>
///     <para>
///         The mark is what a persistence engine other than EF Core reads to apply the parent's visibility to the child. On EF
///         Core the generated <c>ParentVisibilityFilter</c> does it. The attribute names no engine, so one type serves every
///         engine an application references.
///     </para>
/// </remarks>
/// <typeparam name="TParent">The entity the trait is declared on.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TraitOfAttribute<TParent> : Attribute
    where TParent : class
{
}
