namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Allows scoped disabling of query filters.
///     Uses AsyncLocal for thread-safe, scope-based toggling.
/// </summary>
/// <remarks>
///     <para>
///         Filters are disabled within the scope of the returned <see cref="IDisposable"/>.
///         Once the scope is disposed, filters are re-enabled automatically.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Admin sees soft-deleted entities in this scope
/// using (filterToggle.Disable&lt;SoftDeleteFilter_Invoice&gt;())
/// {
///     var deleted = await repo.GetByIdAsync(id, ct);
/// }
/// // Filters are active again here
/// </code>
/// </example>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IQueryFilterToggle
{
    /// <summary>
    ///     Disables a specific filter type within the returned scope.
    /// </summary>
    /// <typeparam name="TFilter">The filter type to disable.</typeparam>
    /// <returns>A disposable scope — filter is re-enabled on dispose.</returns>
    /// <remarks>
    ///     <para>
    ///         ⚠️ It lifts <b>this</b> filter and no other. An entity commonly carries several —
    ///         ownership, scope visibility, tenant — so naming one leaves the rest applied. That is
    ///         the point of the typed form and it is also its trap: the difference from
    ///         <see cref="DisableAll" /> is invisible at the call site, and the narrow form looks like
    ///         the careful choice. <see cref="DisableAll" /> says what to use when the answer is
    ///         "none of them".
    ///     </para>
    ///     <para>
    ///         One exception, and it is not an inconsistency: naming a <c>IScopeVisibilityFilter</c>
    ///         lifts the whole additive group. Those filters are OR-composed — a row is visible if it
    ///         matches any of them — so removing one disjunct <em>narrows</em> the condition. There is
    ///         no partial reading of "lift this alternative", and doing it literally reported a
    ///         legitimate row as a conflict.
    ///     </para>
    /// </remarks>
    IDisposable Disable<TFilter>() where TFilter : IQueryFilter;

    /// <summary>
    ///     Disables a specific filter type by <see cref="Type"/> within the returned scope.
    /// </summary>
    /// <param name="filterType">The filter type to disable.</param>
    /// <returns>A disposable scope — filter is re-enabled on dispose.</returns>
    IDisposable Disable(Type filterType);

    /// <summary>
    ///     Disables all query filters within the returned scope.
    /// </summary>
    /// <returns>A disposable scope — filters are re-enabled on dispose.</returns>
    /// <remarks>
    ///     <para>
    ///         The form to use when the caller must see rows regardless of who is asking: a
    ///         maintenance sweep, a seeding step, a report that is authorised at its own boundary.
    ///     </para>
    ///     <para>
    ///         An <c>IPermissionProvider</c> reading its own rows does not need it: the pipeline skips
    ///         permission filters while a resolution is in progress, so a provider needs no lift at
    ///         all.
    ///     </para>
    /// </remarks>
    IDisposable DisableAll();

    /// <summary>
    ///     Lifts an EF Core <b>named</b> global query filter within the returned scope.
    /// </summary>
    /// <param name="filterName">
    ///     The name the filter was installed under, as written in <c>HasQueryFilter(name, …)</c>.
    /// </param>
    /// <returns>A disposable scope — the filter applies again on dispose.</returns>
    /// <remarks>
    ///     <para>
    ///         Separate from <see cref="Disable(Type)" /> because the two reach different machinery:
    ///         a filter type is disabled inside <c>DefaultQueryFilterProvider</c>, while a name is
    ///         lifted by handing it to <c>IgnoreQueryFilters</c>, which is the only way to unwind a
    ///         filter EF Core itself installed on the model.
    ///     </para>
    ///     <para>
    ///         Names are chosen by the generator on both sides — the one in the entity configuration
    ///         and the one passed here — so nothing is spelled twice by hand. Passing a name EF does
    ///         not know throws when the query runs, which is why this is not a public convenience.
    ///     </para>
    /// </remarks>
    IDisposable DisableQueryFilter(string filterName)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support lifting EF Core named query filters.");

    /// <summary>
    ///     Lifts a declared <c>[VisibleWhen&lt;TRule&gt;]</c> rule within the returned scope.
    /// </summary>
    /// <typeparam name="TRule">The rule to see past.</typeparam>
    /// <returns>A disposable scope — the rule applies again on dispose.</returns>
    /// <remarks>
    ///     The typed way to say what <c>[WithoutFilter&lt;TRule&gt;]</c> says on an operation. Note
    ///     that <see cref="Disable{TFilter}" /> is NOT the way: a declared rule is installed on the EF
    ///     model rather than registered in the filter provider, so disabling its type would reach
    ///     nothing at all. PRAG0720 says so at the call site rather than leaving it to be discovered.
    /// </remarks>
    IDisposable DisableVisibilityRule<TRule>() where TRule : class
        => DisableQueryFilter(VisibilityFilterName.Of<TRule>());

    /// <summary>
    ///     Checks if a specific filter type is currently disabled.
    /// </summary>
    /// <typeparam name="TFilter">The filter type to check.</typeparam>
    /// <returns>True if the filter is disabled in the current scope.</returns>
    bool IsDisabled<TFilter>() where TFilter : IQueryFilter;

    /// <summary>
    ///     Checks if a specific filter type is currently disabled.
    /// </summary>
    /// <param name="filterType">The filter type to check.</param>
    /// <returns>True if the filter is disabled in the current scope.</returns>
    bool IsDisabled(Type filterType);

    /// <summary>
    ///     Checks if all filters are currently disabled.
    /// </summary>
    bool AllDisabled { get; }

    /// <summary>
    ///     Gets the set of filter types currently disabled in this scope.
    ///     Used by the repository to populate <see cref="FilterContext.DisabledFilters"/>.
    /// </summary>
    IReadOnlySet<Type> GetDisabledFilterTypes();

    /// <summary>
    ///     Gets the EF Core named query filters lifted in this scope, for
    ///     <see cref="FilterContext.DisabledQueryFilterNames" />.
    /// </summary>
    IReadOnlySet<string> GetDisabledQueryFilterNames() => EmptyNames;

    /// <summary>Shared empty set, so the default implementation allocates nothing.</summary>
    private static readonly IReadOnlySet<string> EmptyNames = new HashSet<string>();

    /// <summary>
    ///     Sets the <see cref="FilterMode"/> for the current scope.
    ///     Determines which categories of filters are active (Normal, Admin, Background, Raw).
    /// </summary>
    /// <param name="mode">The filter mode to activate.</param>
    /// <returns>A disposable scope — mode reverts to previous value on dispose.</returns>
    IDisposable UseMode(FilterMode mode);

    /// <summary>
    ///     Gets the current <see cref="FilterMode"/> in the active scope.
    ///     Defaults to <see cref="FilterMode.Normal"/> when no mode is explicitly set.
    /// </summary>
    FilterMode CurrentMode { get; }
}
