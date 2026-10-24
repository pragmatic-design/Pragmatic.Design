namespace Pragmatic.Persistence.Sql.Filters;

/// <summary>
///     Who is asking and what they lifted, for the filters of Pragmatic's entities: the state of each filter of a statement, and the values
///     the active ones read. The statement's text is chosen by the states (one variant per combination, assembled once), and the values are
///     its parameters. Without one (<see cref="SqlSession.Filters" /> null) every filter is active and every value empty: a tenant, owner or
///     scope filter then matches nothing.
/// </summary>
public interface ISqlFilters
{
    /// <summary>The state of <paramref name="slot" /> for the execution about to run.</summary>
    SqlFilterState StateOf(SqlFilterSlot slot);

    /// <summary>The current tenant; null when none is resolved (a tenant filter then matches nothing).</summary>
    string? TenantId { get; }

    /// <summary>The current user's identifier; null when anonymous.</summary>
    string? UserId { get; }

    /// <summary>The access scopes the current user holds.</summary>
    IReadOnlyCollection<string> Scopes { get; }

    /// <summary>The instant the execution reads at, the caller's clock (<c>FilterContext.Now</c>): what a temporal filter compares against.</summary>
    DateTimeOffset Now { get; }
}
