namespace Pragmatic.Authorization;

/// <summary>
///     The values of a <see cref="PermissionSetAttribute" /> list, carried in the assembly's metadata so
///     that another compilation can read them. <b>Written by the generator</b>, not by hand.
/// </summary>
/// <remarks>
///     This is the module-declares / host-composes shape: the assembly that owns the list states what it
///     holds, and the compilation whose role reads that list takes the values from the statement rather
///     than from syntax it does not have.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PermissionSetValuesAttribute : Attribute
{
    /// <param name="member">The list's fully qualified name, as the reading compilation sees it.</param>
    /// <param name="permissions">The values it holds, in the order it declares them.</param>
    public PermissionSetValuesAttribute(string member, params string[] permissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentNullException.ThrowIfNull(permissions);

        Member = member;
        Permissions = permissions;
    }

    /// <summary>The list's fully qualified name — <c>Company.Grants.Shared.Granted</c>.</summary>
    public string Member { get; }

    /// <summary>The permission values the list holds.</summary>
    public IReadOnlyList<string> Permissions { get; }
}
