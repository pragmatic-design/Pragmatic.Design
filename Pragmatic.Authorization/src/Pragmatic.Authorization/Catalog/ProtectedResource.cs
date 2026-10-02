using Pragmatic.Authorization.Serialization;

namespace Pragmatic.Authorization.Catalog;

/// <summary>
///     Describes a protected resource in the system (endpoint, action, etc.).
///     Populated by the SG (static) and/or runtime registration (dynamic).
/// </summary>
/// <param name="ResourceType">The type of resource (e.g., "Endpoint", "Action", "Mutation").</param>
/// <param name="Identifier">Unique identifier (e.g., route pattern, action FQN).</param>
/// <param name="DisplayName">Human-readable name for UI display.</param>
/// <param name="Category">Grouping category (e.g., boundary or module name).</param>
public sealed record ProtectedResource(
    string ResourceType,
    string Identifier,
    string DisplayName,
    string? Category)
{
    /// <summary>
    ///     Permissions statically required by this resource (from [RequirePermission]).
    /// </summary>
    public IReadOnlyList<string> StaticPermissions { get; init; } = [];

    /// <summary>
    ///     The policy expression assigned to this resource (if any).
    ///     Stored as <see cref="PolicyExpression" /> for serialization.
    /// </summary>
    public PolicyExpression? PolicyExpression { get; init; }

    /// <summary>
    ///     The boundary/module this resource belongs to (e.g., "booking", "billing").
    ///     Used for resource grouping, subscription modules, and permission hierarchy.
    /// </summary>
    public string? ResourceGroup { get; init; }
}
