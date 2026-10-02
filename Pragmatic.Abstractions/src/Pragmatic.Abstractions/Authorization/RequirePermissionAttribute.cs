namespace Pragmatic.Authorization;

/// <summary>
///     Requires ALL specified permissions to access the resource (AND logic).
///     Can be applied to endpoints, domain actions, and mutations.
/// </summary>
/// <remarks>
///     <para>
///         <b>Mode 1 — Full permission name</b> (enforcement only):
///         <c>[RequirePermission("booking.reservation.create")]</c>
///         or with SG-generated constants:
///         <c>[RequirePermission(BookingPermissions.Reservation.Create)]</c>
///     </para>
///     <para>
///         <b>Mode 2 — Requirement and declaration</b>:
///         <c>[RequirePermission("billing.invoice.refund", Description = "Refund a paid invoice")]</c>
///         declares the permission exactly as <see cref="PermissionAttribute" /> would — the full value, a
///         <c>const</c> in the one <c>{Boundary}Permissions</c> class, an entry in the permission registry — and
///         requires it. Nothing is derived: the value is the first argument as written. A permission more than
///         one operation requires belongs in <c>[assembly: Permission]</c>, and the operations name its constant.
///     </para>
///     <para>
///         <b>Where this is consumed.</b> Four places read the attribute, and only the first one
///         enforces anything:
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Enforcement</b> — <c>ActionsFeature.cs</c> collects the declarations into
///             <c>PermissionRequirementRegistryTemplate</c>, which emits
///             <c>GeneratedPermissionRequirementRegistry</c>; <c>ActionsRegistrationTemplate</c>
///             registers it, and at run time <c>PermissionAuthorizationFilter</c> and
///             <c>MutationInvoker.Validation</c> resolve <c>IPermissionRequirementRegistry</c> and
///             deny the call when a required permission is missing.
///         </item>
///         <item>
///             <b>Declaration</b> (Mode 2) — <c>IdentityFeature.DeclaredPermissions</c> reads a
///             <c>Description</c> into the declared permissions, as it reads <c>[assembly: Permission]</c>;
///             <c>PermissionsClassFeature</c> writes the constant, <c>IdentityFeature</c> the registry entry.
///         </item>
///         <item><c>AutocompleteEndpointTemplate</c> — carries the requirement onto the generated
///             autocomplete route.</item>
///         <item><c>ActionTransform.Validation</c> — shape checks on the declaration itself.</item>
///     </list>
///     <para>
///         <b>Enforcement depends on the generated registry existing, and its absence fails
///         closed.</b> The generator emits a registry for every assembly that has actions — empty
///         when none of them declares anything — so a missing registry has exactly one cause: the
///         generator did not run, or its output was discarded. A duplicate hint name does the
///         latter, and Roslyn reports it as a warning, so the build stays green. In that case
///         <c>UnavailablePermissionRequirementRegistry</c> stands in and refuses every question
///         rather than answering "nothing required", which would let every
///         <c>[RequirePermission]</c> in the assembly pass.
///     </para>
/// </remarks>
[AttributeUsage(
    // Also on a static specification carrying [Query]: the derived query's route asks for the
    // permission written beside the rule, where the author expects to read it.
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property,
    Inherited = false)]
public sealed class RequirePermissionAttribute : Attribute
{
    /// <param name="permissions">
    ///     One or more permission names, all of which must be present (AND logic).
    ///     Must contain at least one non-empty entry.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="permissions"/> is empty or contains a null/empty/whitespace
    ///     entry. Declaring zero permissions would otherwise silently authorize every caller,
    ///     so a misconfigured <c>[RequirePermission()]</c> fails fast at type initialization.
    /// </exception>
    public RequirePermissionAttribute(params string[] permissions)
    {
        if (permissions is null || permissions.Length == 0)
            throw new ArgumentException(
                "[RequirePermission] requires at least one permission. " +
                "An empty permission set would silently grant access.",
                nameof(permissions));

        foreach (var permission in permissions)
            if (string.IsNullOrWhiteSpace(permission))
                throw new ArgumentException(
                    "[RequirePermission] permissions must not contain null, empty, or whitespace entries.",
                    nameof(permissions));

        Permissions = permissions;
    }

    /// <summary>
    ///     Gets the required permissions (all must be present).
    /// </summary>
    public string[] Permissions { get; }

    /// <summary>
    ///     Declares the permission as well as requiring it (Mode 2): its constant and its registry entry are
    ///     generated, with this description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     The group a role screen lists the declared permission under (Mode 2). None when unset.
    /// </summary>
    public string? Category { get; set; }
}
