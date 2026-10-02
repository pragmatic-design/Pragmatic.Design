namespace Pragmatic.Authorization;

/// <summary>
///     The operation answers for the permissions of what it invokes, and those are not asked separately.
/// </summary>
/// <remarks>
///     <para>
///         Without this attribute the permission of every invoked operation is required of the caller:
///         someone who can update the order but not the address is refused. It is the default because
///         it is the safe one — the permission holds wherever the operation is reached.
///     </para>
///     <para>
///         ⚠️ With this attribute the rule is <b>absorbed</b>: whoever holds the permission on the
///         operation also reaches what it calls. It must be declared, deliberately: saying «this
///         operation answers for what it touches» is a domain decision, and silence must not be able
///         to make it in the author's place.
///     </para>
///     <para>
///         <b>The nested child is the most common case, not the only one.</b> The generated invoker
///         opens <c>ICallContext.EnterInternalCall()</c> around the <b>whole body</b> of the operation,
///         first and before delegating, so every call the body makes falls inside it: a mutation's
///         nested children, a composite's steps, and calls on another boundary's
///         <c>I{Boundary}Actions</c>. The last case is the one the remark generated on every boundary
///         interface points to, because the facade does not enter an internal call by itself.
///     </para>
///     <para>
///         ⚠️ <b>The operation's own permission is untouched.</b> Its
///         <see cref="RequirePermissionAttribute" /> is checked <em>before</em> the body starts, and
///         absorbing applies only from there on. An absorbing operation is not an open operation: if it
///         were, «answers for what it touches» would be satisfied by one that asks nobody for anything.
///     </para>
///     <para>
///         On a nested child, absorbing is <b>the absence of the question</b>: the invoker does not emit
///         the <c>NestedOperations</c> list, so there is no second place where the permission could be
///         evaluated inconsistently. The precedent is <c>[CompositeAction]</c>, which absorbs its steps'
///         permissions.
///     </para>
///     <example>
///         The nested child — «whoever updates the order may write its address»:
///         <code>
/// [Mutation(Mode = MutationMode.Update)]
/// [AbsorbsChildPermissions]
/// public partial class SetAddressMutation : Mutation&lt;Order&gt;
/// {
///     public required Guid Id { get; init; }
///     public WriteAddressMutation? Address { get; init; }   // carries [RequirePermission]
/// }
/// </code>
///         Crossing a boundary — «whoever writes a story answers for the terms writing it proposes».
///         Without the attribute the caller also needs the other boundary's permission, because a call
///         through the public interface is not an internal call:
///         <code>
/// [DomainAction]
/// [RequirePermission(WorkPermissions.WorkItem.Create)]   // still required
/// [AbsorbsChildPermissions]
/// public partial class WriteStoryAction : DomainAction&lt;WriteStoryResult&gt;
/// {
///     private IKnowledgeActions _knowledge = null!;
///     // _knowledge.Corpus.IngestText(...) requires knowledge.knowledge-item.create,
///     // and this operation answers for it in the caller's place.
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AbsorbsChildPermissionsAttribute : Attribute;
