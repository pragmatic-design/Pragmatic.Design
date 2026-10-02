using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     A named rule saying which rows of an entity are visible at all, applied to every query.
/// </summary>
/// <typeparam name="T">The entity the rule applies to.</typeparam>
/// <remarks>
///     <para>
///         <b>The rule is the filter.</b> It implements <see cref="IQueryFilter{T}" /> itself rather
///         than being wrapped by a generated one, so there is a single name for it — the one written
///         in <c>[VisibleWhen&lt;TRule&gt;]</c> on the entity, and the same one written in
///         <c>[WithoutFilter&lt;TRule&gt;]</c> or <c>IQueryFilterToggle.DisableVisibilityRule&lt;TRule&gt;()</c>
///         where a caller is allowed to see past it. Two places, one name, both checked by the compiler.
///     </para>
///     <para>
///         ⚠️ <b>Not <c>Disable&lt;TRule&gt;()</c>.</b> That takes a filter out of
///         <c>DefaultQueryFilterProvider</c>, and a declared rule is not in it — see below. The call
///         compiles and changes nothing, so PRAG0720 rejects it.
///     </para>
///     <para>
///         <b>Visibility is not endpoint purpose.</b> A rule here says a row should not be seen at
///         all — archived, superseded, belonging to a closed period. It is the wrong home for "which
///         rows may this one endpoint offer": that would make every other read need an opt-out, and
///         the first caller who forgets one publishes the hole. When only one operation wants a
///         narrower set, the answer is a query.
///     </para>
///     <para>
///         <b>Where it is enforced, and why there.</b> A rule named by <c>[VisibleWhen&lt;TRule&gt;]</c>
///         is installed by the generated entity configuration as an EF Core <i>named global query
///         filter</i>, beside soft-delete and tenant — not registered in the Pragmatic filter provider.
///         That is what makes it reach a collection: the provider is consulted at the root of a query,
///         and a projected collection never passes through it, because the executor filters the entity
///         queryable and adds the projection afterwards. EF applies a model-level filter to every query
///         touching the entity, an <c>Include</c> and a projected subquery included, and to a raw
///         <c>context.Set&lt;T&gt;()</c> as well.
///     </para>
///     <para>
///         ⚠️ <b>The cost of that reach is a constructor constraint.</b> The predicate is read once,
///         from <c>new TRule().ToExpression()</c>, while EF builds the model — so a rule must be
///         concrete, non-generic, and constructible with no arguments (PRAG0718). A rule that wants
///         the current user cannot be one at all: the model is cached per context type, so anything
///         scoped captured here would be served to every later request. Predicates that depend on the
///         caller are the ownership and scope filters, which compose additively and are evaluated per
///         request.
///     </para>
///     <para>
///         <b>It hides rows from writes as well as reads.</b> An update loads the entity through the
///         same filtered query, so a row that stops satisfying the rule can no longer be reached to
///         change it back. Whatever manages those rows needs <c>[WithoutFilter&lt;TRule&gt;]</c>, or
///         the state the rule keys on is a one-way door.
///     </para>
///     <para>
///         <b>Not bypassed by a filter mode.</b> <c>FilterMode.Raw</c> lifts every query filter and
///         <c>Background</c> lifts the tenant one by name; nothing else touches a rule. Turning one off
///         is a deliberate act, named at the point that does it — which also keeps the root of a query
///         and its <c>Include</c> consistent: in <c>Admin</c> a rule guards both.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public sealed class ConfirmedOnly : VisibilityRule&lt;KnowledgeItem&gt;
/// {
///     public override Expression&lt;Func&lt;KnowledgeItem, bool&gt;&gt; ToExpression()
///         =&gt; item =&gt; item.IsConfirmed;
/// }
///     </code>
/// </example>
public abstract class VisibilityRule<T> : IQueryFilter<T>
    where T : class
{
    /// <summary>
    ///     Structural filters run first: soft-delete at 100, ownership at 200, scopes at 250 and 260.
    ///     A domain rule sits after them, so the cheap and universal predicates narrow the set before
    ///     it asks its own question.
    /// </summary>
    public virtual int Priority => 300;

    /// <inheritdoc />
    public virtual FilterScope Scope => FilterScope.Default;

    /// <summary>
    ///     The predicate: an entity is visible when this holds.
    /// </summary>
    /// <remarks>
    ///     Named as it is on <c>Specification&lt;T&gt;</c> and <c>DataScopeRule&lt;T&gt;</c>, because a
    ///     class carrying a predicate already has a name in this codebase and a second one would be a
    ///     second concept.
    /// </remarks>
    public abstract Expression<Func<T, bool>> ToExpression();

    /// <inheritdoc />
    public Expression<Func<T, bool>> GetFilter() => ToExpression();
}
