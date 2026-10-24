using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The filters of Pragmatic's entities under the Sql engine, decided as Pragmatic decides them with EF Core (<see cref="PragmaticFilterDecision" />,
///     shared with the documents engine): the mode and the lifts of <see cref="IQueryFilterToggle" />, the user of <see cref="ICurrentUser" />, the
///     tenant of <see cref="ITenantContext" />, the scopes of <see cref="IUserScopeResolver" />, <see cref="QueryFilterOptions.FailClosedWhenAnonymous" />.
/// </summary>
/// <remarks>
///     Scoped like the services it reads: one per request or unit of work. Nothing is cached, so a lift opened during the scope
///     (<c>using (toggle.UseMode(FilterMode.Admin))</c>) applies from the next execution.
/// </remarks>
public sealed class PragmaticSqlFilters(
    ICurrentUser? currentUser = null,
    ITenantContext? tenantContext = null,
    IUserScopeResolver? scopeResolver = null,
    IQueryFilterToggle? toggle = null,
    QueryFilterOptions? options = null,
    TimeProvider? timeProvider = null,
    IEnumerable<global::Pragmatic.Persistence.Scopes.DataScopeRule>? scopeRules = null) : ISqlFilters
{
    private readonly PragmaticFilterDecision _decision = new(currentUser, toggle, options, CompiledScopeRules.Registry, scopeRules);

    /// <summary>The mode in effect: the toggle's, Normal without one.</summary>
    public FilterMode Mode => _decision.Mode;

    public string? TenantId => tenantContext?.TenantId;

    public string? UserId => currentUser is { IsAuthenticated: true } ? currentUser.Id : null;

    public IReadOnlyCollection<string> Scopes
        => currentUser is { IsAuthenticated: true } && scopeResolver is not null ? scopeResolver.ResolveAccessScopes(currentUser) : [];

    /// <summary>The caller's clock now, as <c>FilterContext.At(TimeProvider)</c> reads it; the system's without one.</summary>
    public DateTimeOffset Now => (timeProvider ?? TimeProvider.System).GetUtcNow();

    public SqlFilterState StateOf(SqlFilterSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.Family == SqlFilterFamily.ComputedScope)
            return (SqlFilterState)_decision.ComputedScopeState(slot.Filter, Scopes);
        if (slot.Family == SqlFilterFamily.Permission)
            _decision.EnsureCompiled(slot.Entity); // a computed rule registered for it, not compiled: refused, not read past
        return (SqlFilterState)_decision.StateOf((PragmaticFamily)slot.Family, slot.Entity, slot.Filter, slot.Name, slot.Bypass);
    }
}
