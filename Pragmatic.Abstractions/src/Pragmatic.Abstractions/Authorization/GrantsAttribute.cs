namespace Pragmatic.Authorization;

/// <summary>
///     On a <see cref="RoleAttribute" /> class: the permissions the role grants. Repeatable.
/// </summary>
/// <remarks>
///     Name the generated constants — <c>[Grants(LeavePermissions.LeaveRequest.Decide)]</c> — or write a value,
///     wildcards included (<c>"leave.employee.*"</c>). A constant the generator does not write is
///     <c>PRAG1008</c>: dropping it would catalogue a role granting less than it was declared to.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GrantsAttribute : Attribute
{
    /// <param name="permissions">One or more permissions; none null, empty or whitespace.</param>
    public GrantsAttribute(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Length == 0)
            throw new ArgumentException("[Grants] needs at least one permission.", nameof(permissions));
        foreach (var permission in permissions)
            ArgumentException.ThrowIfNullOrWhiteSpace(permission, nameof(permissions));

        Permissions = permissions;
    }

    /// <summary>The permissions granted.</summary>
    public string[] Permissions { get; }
}
