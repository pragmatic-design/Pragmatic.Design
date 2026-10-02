namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Runtime context for filter evaluation.
///     Captures the current user, tenant, disabled filters, and filter mode.
///     Used by the FilterMap to determine which filters to apply.
/// </summary>
public sealed record FilterContext
{
    /// <summary>Current tenant identifier, if multi-tenancy is active.</summary>
    public string? TenantId { get; init; }

    /// <summary>Current user identifier, if authenticated.</summary>
    public string? UserId { get; init; }

    /// <summary>Current UTC timestamp for temporal filter evaluation.</summary>
    /// <remarks>
    ///     ⚠️ <c>required</c>, and it is the whole point: a default of <c>DateTimeOffset.UtcNow</c>
    ///     would make any construction that forgot it silently read the wall clock. It could not fail,
    ///     it could not be pinned, and it would look right at the call site because the property is
    ///     named for the value it should carry. Omitting it is a compile error.
    /// </remarks>
    public required DateTimeOffset Now { get; init; }

    /// <summary>A context at a given instant.</summary>
    /// <param name="instant">The instant a temporal filter should evaluate against.</param>
    /// <remarks>
    ///     The shape for a caller that has an instant, and the one that absorbs the noise
    ///     <c>required</c> would otherwise put on every construction. ⚠️ It reads nothing: two calls
    ///     with the same instant produce equal contexts, which is what a parameterless default
    ///     reintroducing the clock could not do.
    /// </remarks>
    public static FilterContext At(DateTimeOffset instant) => new() { Now = instant };

    /// <summary>A context at the instant a provider reports.</summary>
    /// <param name="clock">The provider to read once.</param>
    /// <remarks>
    ///     For a caller holding a <see cref="TimeProvider" /> — a pinned one in a test, the system one
    ///     in production — so the unwrapping is written here instead of at every call site.
    /// </remarks>
    public static FilterContext At(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return At(clock.GetUtcNow());
    }

    /// <summary>
    ///     Filter types explicitly disabled in the current scope.
    ///     Populated from <see cref="IQueryFilterToggle"/>.
    /// </summary>
    public IReadOnlySet<Type> DisabledFilters { get; init; } = new HashSet<Type>();

    /// <summary>
    ///     EF Core <b>named</b> global query filters lifted in the current scope, populated from
    ///     <see cref="IQueryFilterToggle.GetDisabledQueryFilterNames" />.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="DisabledFilters" />, which names filter <see cref="Type" />s the
    ///     Pragmatic provider evaluates. A filter EF installed on the model is not in that provider
    ///     and cannot be dropped there: the only way past it is <c>IgnoreQueryFilters</c>, which takes
    ///     names. A declared <c>[VisibleWhen&lt;TRule&gt;]</c> rule lives on that side, so the opt-out
    ///     for one travels here.
    /// </remarks>
    public IReadOnlySet<string> DisabledQueryFilterNames { get; init; } = new HashSet<string>();

    /// <summary>
    ///     Controls which categories of filters are active.
    ///     See <see cref="FilterMode"/> for details.
    /// </summary>
    public FilterMode Mode { get; init; } = FilterMode.Normal;

    /// <summary>Whether all filters are bypassed (Mode is Raw or all disabled).</summary>
    public bool IsRaw => Mode == FilterMode.Raw;

    /// <summary>
    ///     Whether the dynamic filter providers are skipped when composing the navigation map.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The name is older than what it does. It does <b>not</b> lift a declared
    ///     <c>[VisibleWhen&lt;TRule&gt;]</c> rule: that is an EF Core named query filter, and only
    ///     <c>Raw</c> — or naming it through <c>[WithoutFilter&lt;TRule&gt;]</c> — gets past one. Its
    ///     single reader is <c>FilterMapComposer</c>, where it guards against a provider that ignores
    ///     the context. Same expression as <see cref="SkipPermissionBased" />, kept apart because the
    ///     two answer different questions of the same mode.
    /// </remarks>
    public bool SkipVisibility => Mode >= FilterMode.Admin;

    /// <summary>Whether permission-based filters should be skipped.</summary>
    public bool SkipPermissionBased => Mode >= FilterMode.Admin;

    /// <summary>Whether tenant filters should be skipped.</summary>
    public bool SkipTenant => Mode >= FilterMode.Background;

    /// <summary>Whether another context carries the same values.</summary>
    /// <param name="other">The context to compare against.</param>
    /// <returns><c>true</c> when every property holds the same value.</returns>
    /// <remarks>
    ///     ⚠️ Written by hand because the synthesized one could not work here. It compares
    ///     <see cref="DisabledFilters" /> and <see cref="DisabledQueryFilterNames" /> by reference, and
    ///     both default to a fresh set — so two contexts built the same way were never equal, and the
    ///     failure printed two identical objects.
    ///     <para>
    ///         Hand-written equality replaces the synthesized one wholesale: a property added to this
    ///         record and forgotten here is a silent widening, not a compile error.
    ///     </para>
    /// </remarks>
    public bool Equals(FilterContext? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        return other is not null
               && Now == other.Now
               && Mode == other.Mode
               && TenantId == other.TenantId
               && UserId == other.UserId
               && DisabledFilters.SetEquals(other.DisabledFilters)
               && DisabledQueryFilterNames.SetEquals(other.DisabledQueryFilterNames);
    }

    /// <summary>A hash code over the values that cannot move.</summary>
    /// <returns>A hash code consistent with <see cref="Equals(FilterContext)" />.</returns>
    /// <remarks>
    ///     ⚠️ <b>The two sets are deliberately left out</b>, and this is the part a reader would not
    ///     guess. The properties are <see cref="IReadOnlySet{T}" /> by contract, but nothing stops a
    ///     caller from passing a <see cref="HashSet{T}" /> it goes on mutating: hashing the members
    ///     would move the key underneath a context already stored in a dictionary, and the entry would
    ///     become unfindable through the very object that put it there.
    ///     <para>
    ///         The contract only runs one way — equal objects must agree on their hash code — so
    ///         omitting them is legal. The price is that two contexts differing only in a disabled
    ///         filter collide, and <see cref="Equals(FilterContext)" /> separates them.
    ///     </para>
    /// </remarks>
    public override int GetHashCode() => HashCode.Combine(Now, Mode, TenantId, UserId);
}
