using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The filters of Pragmatic's entities under the documents engine, decided as Pragmatic decides them with EF Core and as the Sql engine does
///     (<see cref="PragmaticFilterDecision" />, one source for both): the mode and the lifts of <see cref="IQueryFilterToggle" />, the user of
///     <see cref="ICurrentUser" />, the tenant of <see cref="ITenantContext" />, the scopes of <see cref="IUserScopeResolver" />.
/// </summary>
/// <remarks>Scoped like the services it reads: nothing is cached, a lift opened during the scope applies from the next read.</remarks>
public sealed class PragmaticDocFilters(
    ICurrentUser? currentUser = null,
    ITenantContext? tenantContext = null,
    IUserScopeResolver? scopeResolver = null,
    IQueryFilterToggle? toggle = null,
    QueryFilterOptions? options = null,
    TimeProvider? timeProvider = null,
    IEnumerable<global::Pragmatic.Persistence.Scopes.DataScopeRule>? scopeRules = null) : IDocFilters
{
    private readonly PragmaticFilterDecision _decision = new(currentUser, toggle, options, CompiledScopeRules.Registry, scopeRules);

    public FilterMode Mode => _decision.Mode;

    public string? TenantId => tenantContext?.TenantId;

    public string? UserId => currentUser is { IsAuthenticated: true } ? currentUser.Id : null;

    public IReadOnlyCollection<string> Scopes
        => currentUser is { IsAuthenticated: true } && scopeResolver is not null ? scopeResolver.ResolveAccessScopes(currentUser) : [];

    /// <summary>The caller's clock now, as <c>FilterContext.At(TimeProvider)</c> reads it; the system's without one.</summary>
    public DateTimeOffset Now => (timeProvider ?? TimeProvider.System).GetUtcNow();

    public DocFilterState StateOf(DocFilterSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.Family == DocFilterFamily.ComputedScope)
            return (DocFilterState)_decision.ComputedScopeState(slot.Filter, Scopes);
        if (slot.Family == DocFilterFamily.Permission)
            _decision.EnsureCompiled(slot.Entity); // a computed rule registered for it, not compiled: refused, not read past
        return (DocFilterState)_decision.StateOf((PragmaticFamily)slot.Family, slot.Entity, slot.Filter, slot.Name, slot.Bypass);
    }
}
