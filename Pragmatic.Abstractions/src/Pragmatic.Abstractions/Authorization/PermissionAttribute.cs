namespace Pragmatic.Authorization;

/// <summary>
///     Declares a custom permission of this assembly — one that is not an entity's CRUD permission:
///     <c>[assembly: Permission("leave.personal-data.erase", "Erase an employee's personal data", Category = "Privacy")]</c>.
/// </summary>
/// <remarks>
///     <para>
///         The generator adds the constant to the one <c>{Boundary}Permissions</c> class the CRUD permissions
///         are in — <c>LeavePermissions.PersonalData.Erase</c>, a <c>const</c> usable in
///         <c>[RequirePermission]</c> — and the entry (name, description, category) to the permission
///         registry the host exposes, where a role screen lists it.
///     </para>
///     <para>
///         The value is <c>{boundary}.{resource}.{verb}</c>, its first segment one of the assembly's
///         boundaries (<c>PRAG1004</c> otherwise); a resource that is an entity puts the constant in that
///         entity's class, beside its CRUD permissions. A value declared twice — here, in a
///         <c>[RequirePermission(Description = …)]</c>, or as a CRUD permission — is <c>PRAG1001</c>; one whose
///         constant would take a name the class already uses (<c>leave.employee</c> beside the entity
///         <c>Employee</c>'s class) is <c>PRAG1005</c>.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PermissionAttribute : Attribute
{
    /// <param name="value">The permission's value — <c>{boundary}.{resource}.{verb}</c>.</param>
    /// <param name="description">What holding it allows, as a role screen lists it.</param>
    public PermissionAttribute(string value, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Value = value;
        Description = description;
    }

    /// <summary>The permission's value.</summary>
    public string Value { get; }

    /// <summary>What holding it allows.</summary>
    public string Description { get; }

    /// <summary>The group a role screen lists it under — <c>"Privacy"</c>, <c>"Leave"</c>.</summary>
    public string? Category { get; set; }
}
