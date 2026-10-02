namespace Pragmatic.Authorization.Stores;

/// <summary>
///     In-memory <see cref="IGroupRoleStore"/> for development, testing, and configuration-driven setups.
///     Populate via <c>AuthorizationBuilder.MapGroup</c> or programmatically.
/// </summary>
public sealed class InMemoryGroupRoleStore : IGroupRoleStore
{
    private static readonly IReadOnlySet<string> EmptyRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HashSet<string>> _groupRoles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>Adds a group with its roles.</summary>
    public void AddGroup(string groupName, IEnumerable<string> roles)
    {
        lock (_lock)
        {
            if (!_groupRoles.TryGetValue(groupName, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _groupRoles[groupName] = set;
            }

            foreach (var r in roles)
                set.Add(r);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // Return a defensive snapshot: the stored set stays mutable and lock-protected,
            // so callers can never mutate (or cast back into) the store's internal state.
            IReadOnlySet<string> result = _groupRoles.TryGetValue(groupName, out var roles)
                ? new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase)
                : EmptyRoles;

            return ValueTask.FromResult(result);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> GetAllGroupsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<string> groups = _groupRoles.Keys.ToList();
            return ValueTask.FromResult(groups);
        }
    }
}
