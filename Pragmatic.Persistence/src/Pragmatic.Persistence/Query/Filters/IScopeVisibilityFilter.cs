namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Marks a query filter as an <b>additive</b> data-visibility contribution
///     (ownership / materialized access scopes / computed scope rules).
/// </summary>
/// <remarks>
///     <para>
///         AND-composing every filter is correct for <em>restrictive</em> filters (soft-delete, tenant), but
///         wrong for scope visibility: a row should be visible if it matches the materialized
///         scope <em>OR</em> a computed scope rule. AND-composing a materialized
///         <c>ScopedDataFilter</c>/<c>DataAccessFilter</c> with a <c>ComputedScopeFilter</c> hides
///         rows that are only reachable through the computed rule.
///     </para>
///     <para>
///         Filters implementing this marker are grouped and <b>OR-composed together</b>, and the
///         resulting visibility predicate is then AND-composed with the remaining (restrictive)
///         filters. A filter whose <see cref="IQueryFilter{T}.GetFilter"/> returns an unconditional
///         <c>_ =&gt; true</c> contributes nothing to the OR group (it neither widens nor narrows
///         visibility), which keeps an empty/no-rule computed filter from collapsing the group to
///         "all rows".
///     </para>
/// </remarks>
public interface IScopeVisibilityFilter : IQueryFilter;
