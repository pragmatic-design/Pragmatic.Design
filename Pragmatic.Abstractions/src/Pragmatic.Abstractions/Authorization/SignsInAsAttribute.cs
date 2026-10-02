namespace Pragmatic.Authorization;

/// <summary>
///     On a member of the enum a user entity keeps its access level in: the role a user with that value signs in as.
/// </summary>
/// <remarks>
///     <para>
///         The generator writes the mapping — <c>{Enum}RoleNames.RoleNameOf(this {Enum})</c>, an extension — as a switch
///         over every member, answering <c>TRole.Name</c>. Once one member of an enum has it, every member must:
///         one without is <c>PRAG1014</c>, so no value the enum declares can reach a sign-in with no role.
///     </para>
///     <code>
/// public enum AccessRole
/// {
///     [SignsInAs&lt;EmployeeRole&gt;] Employee,
///     [SignsInAs&lt;ManagerRole&gt;] Manager,
/// }
/// claims.Roles.Add(employee.Role.RoleNameOf());
/// </code>
/// </remarks>
/// <typeparam name="TRole">The role — a <c>[Role]</c> class or a hand-written <see cref="IRole" />.</typeparam>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class SignsInAsAttribute<TRole> : Attribute
    where TRole : IRole;
