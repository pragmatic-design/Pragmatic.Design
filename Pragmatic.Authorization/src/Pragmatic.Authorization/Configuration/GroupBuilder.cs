namespace Pragmatic.Authorization.Configuration;

/// <summary>
///     Fluent builder for inline group configuration.
/// </summary>
public sealed class GroupBuilder
{
    internal List<string> Roles { get; } = [];
    /// <summary>Adds roles to this group by name.</summary>
    public GroupBuilder WithRoles(params string[] roles)
    {
        Roles.AddRange(roles);
        return this;
    }

    /// <summary>Adds a strongly-typed role to this group.</summary>
    public GroupBuilder WithRole<TRole>() where TRole : IRole
    {
        Roles.Add(TRole.Name);
        return this;
    }

}
