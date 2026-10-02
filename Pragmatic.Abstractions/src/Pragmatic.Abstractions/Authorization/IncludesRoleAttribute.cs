namespace Pragmatic.Authorization;

/// <summary>
///     On a <see cref="RoleAttribute" /> class: the role grants every permission <typeparamref name="TRole" />
///     grants. Repeatable.
/// </summary>
/// <remarks>
///     The included role is a <c>[Role]</c> class or a hand-written <see cref="IRole" /> of this assembly, or a
///     <c>[Role]</c> class of a referenced one — whose attributes are in its metadata. A hand-written role of
///     another assembly has no permissions the generator can read (<c>PRAG1009</c>); roles that include each
///     other are <c>PRAG1007</c>.
/// </remarks>
/// <typeparam name="TRole">The role whose permissions are included.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IncludesRoleAttribute<TRole> : Attribute
    where TRole : IRole;
