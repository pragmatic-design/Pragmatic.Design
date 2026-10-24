using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Filters;

/// <summary>
///     Who is asking and what they lifted, for the filters of Pragmatic's entities: the state of each filter and the values the active ones read.
///     Without one (<see cref="DocSession.Filters" /> null) every filter is active and every value empty: a tenant, owner or scope filter then
///     matches nothing.
/// </summary>
public interface IDocFilters
{
    DocFilterState StateOf(DocFilterSlot slot);

    /// <summary>The current tenant; null when none is resolved (a tenant filter then matches nothing).</summary>
    string? TenantId { get; }

    /// <summary>The current user's identifier; null when anonymous.</summary>
    string? UserId { get; }

    /// <summary>The access scopes the current user holds.</summary>
    IReadOnlyCollection<string> Scopes { get; }

    /// <summary>The instant the read is at, the caller's clock (<c>FilterContext.Now</c>): what a temporal filter compares against.</summary>
    DateTimeOffset Now { get; }
}
