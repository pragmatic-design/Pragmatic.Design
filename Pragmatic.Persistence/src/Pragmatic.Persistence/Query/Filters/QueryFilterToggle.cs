using System.Collections.Immutable;

namespace Pragmatic.Persistence.Query.Filters;

/// <summary>
///     Default implementation of <see cref="IQueryFilterToggle"/>.
///     Uses <see cref="AsyncLocal{T}"/> with copy-on-write immutable state for correct,
///     isolated scope-based toggling.
/// </summary>
/// <remarks>
///     <para>
///         Supports nested scopes — disabling SoftDelete inside an already-disabled Tenant scope
///         disables both. Disposing the inner scope re-enables only SoftDelete.
///     </para>
///     <para>
///         State is immutable and copy-on-write. Each mutation publishes a NEW state
///         object into the <see cref="AsyncLocal{T}"/> and the returned scope restores the previous
///         snapshot on dispose. This makes nested disables of the SAME filter correct (the snapshot,
///         not a refcount-less HashSet remove, restores prior state) and prevents a child async flow
///         from mutating a parent flow's state (every write reassigns the AsyncLocal slot, which is
///         per-flow). Reads are lock-free because published state never mutates.
///     </para>
/// </remarks>
public sealed class QueryFilterToggle : IQueryFilterToggle
{
    private static readonly AsyncLocal<ToggleState?> CurrentState = new();
    private static readonly IReadOnlySet<Type> EmptySet = new HashSet<Type>();
    private static readonly IReadOnlySet<string> EmptyNameSet = new HashSet<string>();

    public IDisposable Disable<TFilter>() where TFilter : IQueryFilter
        => Disable(typeof(TFilter));

    public IDisposable Disable(Type filterType)
    {
        var previous = CurrentState.Value;
        var current = previous ?? ToggleState.Empty;
        CurrentState.Value = current with { DisabledTypes = current.DisabledTypes.Add(filterType) };
        return new FilterScope(() => CurrentState.Value = previous);
    }

    public IDisposable DisableQueryFilter(string filterName)
    {
        var previous = CurrentState.Value;
        var current = previous ?? ToggleState.Empty;
        CurrentState.Value = current with { DisabledNames = current.DisabledNames.Add(filterName) };
        return new FilterScope(() => CurrentState.Value = previous);
    }

    public IDisposable DisableAll()
    {
        var previous = CurrentState.Value;
        var current = previous ?? ToggleState.Empty;
        CurrentState.Value = current with { AllDisabledCount = current.AllDisabledCount + 1 };
        return new FilterScope(() => CurrentState.Value = previous);
    }

    public bool IsDisabled<TFilter>() where TFilter : IQueryFilter
        => IsDisabled(typeof(TFilter));

    public bool IsDisabled(Type filterType)
    {
        var state = CurrentState.Value;
        if (state is null)
            return false;
        if (state.AllDisabledCount > 0)
            return true;
        return state.DisabledTypes.Contains(filterType);
    }

    public bool AllDisabled
    {
        get
        {
            var state = CurrentState.Value;
            return state is not null && state.AllDisabledCount > 0;
        }
    }

    public IDisposable UseMode(FilterMode mode)
    {
        var previous = CurrentState.Value;
        var current = previous ?? ToggleState.Empty;
        CurrentState.Value = current with { Mode = mode };
        return new FilterScope(() => CurrentState.Value = previous);
    }

    public IReadOnlySet<string> GetDisabledQueryFilterNames()
    {
        var state = CurrentState.Value;
        return state is null || state.DisabledNames.IsEmpty
            ? EmptyNameSet
            : state.DisabledNames;
    }

    public IReadOnlySet<Type> GetDisabledFilterTypes()
    {
        var state = CurrentState.Value;
        // DisabledTypes is an immutable set; safe to hand out directly. Adapt to IReadOnlySet.
        return state is null || state.DisabledTypes.IsEmpty
            ? EmptySet
            : state.DisabledTypes;
    }

    public FilterMode CurrentMode
    {
        get
        {
            var state = CurrentState.Value;
            return state?.Mode ?? FilterMode.Normal;
        }
    }

    /// <summary>
    ///     Immutable per-async-flow state tracking disabled filters and filter mode.
    ///     Copy-on-write: mutations produce a new instance via <c>with</c>.
    /// </summary>
    private sealed record ToggleState
    {
        public static readonly ToggleState Empty = new();

        public ImmutableHashSet<Type> DisabledTypes { get; init; } = ImmutableHashSet<Type>.Empty;
        public ImmutableHashSet<string> DisabledNames { get; init; } = ImmutableHashSet<string>.Empty;
        public int AllDisabledCount { get; init; }
        public FilterMode Mode { get; init; } = FilterMode.Normal;
    }

    /// <summary>
    ///     Restores the previous state snapshot when disposed (idempotent).
    /// </summary>
    private sealed class FilterScope(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose();
        }
    }
}
