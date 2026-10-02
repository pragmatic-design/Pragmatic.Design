namespace Invoicing.Registry.Dtos;

/// <summary>
///     Who the caller is, as the token says: the provider's subject, the display name, the roles it carries
///     and the permissions those roles resolve to.
/// </summary>
/// <remarks>
///     Invoicing keeps no user row. There is nothing to read but the token, which is why this is the answer
///     of an action and not a projection of an entity.
/// </remarks>
public sealed record CallerDto(
    string Id,
    string? DisplayName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
