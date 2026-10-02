namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares, on the entity, a <c>VisibilityRule&lt;T&gt;</c> that every query of it must satisfy.
/// </summary>
/// <typeparam name="TRule">
///     The rule. Must derive from <c>VisibilityRule&lt;TEntity&gt;</c> for the entity carrying this
///     attribute, and be concrete, non-generic and constructible with no arguments — its predicate is
///     read once while EF builds the model. PRAG0717 and PRAG0718 check both.
/// </typeparam>
/// <remarks>
///     <para>
///         <b>The attribute is what installs the rule</b>, and a reader of the entity can therefore
///         see that its rows are filtered. The generated entity configuration turns each one into an
///         EF Core named global query filter, so the rule applies to every query touching the entity —
///         root, <c>Include</c>, projected subqueries, and a raw <c>context.Set&lt;T&gt;()</c> alike.
///     </para>
///     <para>
///         ⚠️ A rule merely registered in DI as an <c>IQueryFilter</c> still works, but only at the
///         root of a query — that is the ordinary filter path, and it is not this. Declaring it here is
///         what buys the rest.
///     </para>
///     <para>
///         Repeatable: an entity may have more than one rule, and they compose as an AND. Order is not
///         something to set here — EF Core AND-combines named query filters however they were added,
///         and nothing reads <c>Priority</c> off a declared rule. <c>Priority</c> exists on
///         <c>VisibilityRule&lt;T&gt;</c> because it is an <c>IQueryFilter</c>, where it orders the
///         provider's own filters; on a rule installed by this attribute it decides nothing.
///     </para>
///     <para>
///         <b>A hidden row is hidden from the writes too, and this is the part that surprises.</b> An
///         update or a delete loads the entity through the same filtered query a read does, so an
///         entity that falls out of its own rule cannot be reached by anything: measured on the
///         Showcase as <c>read=404 update=404 delete=404</c>, with the flag that caused it impossible
///         to set back. Declare the rule, then give the operations that manage those rows
///         <c>[WithoutFilter&lt;TRule&gt;]</c> beside the permission they already require — otherwise
///         the state the rule keys on becomes a one-way door.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Entity]
/// [VisibleWhen&lt;ConfirmedOnly&gt;]
/// public partial class KnowledgeItem { … }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class VisibleWhenAttribute<TRule> : Attribute
    where TRule : class;
