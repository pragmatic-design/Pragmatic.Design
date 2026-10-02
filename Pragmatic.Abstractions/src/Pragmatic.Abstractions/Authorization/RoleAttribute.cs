namespace Pragmatic.Authorization;

/// <summary>
///     Declares a role on a <c>partial</c> class: the generator writes its <see cref="IRole" /> members.
/// </summary>
/// <remarks>
///     <para>
///         <c>Name</c> and <c>Description</c> come from here; <c>DefaultPermissions</c> is every permission the
///         class's <see cref="GrantsAttribute" /> name, plus those of the roles its
///         <see cref="IncludesRoleAttribute{TRole}" /> name — flattened, de-duplicated and ordered — and the role
///         registry lists the same set.
///     </para>
///     <code>
/// [Role("manager", "Decides the leave requests of the teams they manage")]
/// [IncludesRole&lt;EmployeeRole&gt;]
/// [Grants(LeavePermissions.LeaveRequest.Decide)]
/// public partial class ManagerRole;
/// </code>
///     <para>
///         A class that is not <c>partial</c> is <c>PRAG1006</c>. A hand-written <see cref="IRole" /> keeps
///         working, and can be included.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RoleAttribute : Attribute
{
    /// <param name="name">The role's name — what a claim carries.</param>
    /// <param name="description">What the role is for, as a role screen lists it.</param>
    public RoleAttribute(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Name = name;
        Description = description;
    }

    /// <summary>The role's name.</summary>
    public string Name { get; }

    /// <summary>What the role is for.</summary>
    public string Description { get; }
}
