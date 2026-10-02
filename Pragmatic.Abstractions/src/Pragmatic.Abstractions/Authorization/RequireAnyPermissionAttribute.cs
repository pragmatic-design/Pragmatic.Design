namespace Pragmatic.Authorization;

/// <summary>
///     Requires ANY of the specified permissions to access the resource (OR logic).
///     Can be applied to endpoints, domain actions, and mutations.
/// </summary>
/// <remarks>
///     <para>
///     For endpoints: the source generator creates an ASP.NET Core authorization policy.
///     For domain actions: the <c>PermissionAuthorizationFilter</c> checks before execution.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [RequireAnyPermission("reports.sales", "reports.all")]
/// public partial class GetSalesReport : Endpoint&lt;ReportDto&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequireAnyPermissionAttribute : Attribute
{
    /// <param name="permissions">
    ///     One or more permission names, at least one of which must be present (OR logic).
    ///     Must contain at least one non-empty entry.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="permissions"/> is empty or contains a null/empty/whitespace
    ///     entry. An empty "any-of" set is unsatisfiable and almost certainly a configuration error,
    ///     so it fails fast at type initialization.
    /// </exception>
    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        if (permissions is null || permissions.Length == 0)
            throw new ArgumentException(
                "[RequireAnyPermission] requires at least one permission.",
                nameof(permissions));

        foreach (var permission in permissions)
            if (string.IsNullOrWhiteSpace(permission))
                throw new ArgumentException(
                    "[RequireAnyPermission] permissions must not contain null, empty, or whitespace entries.",
                    nameof(permissions));

        Permissions = permissions;
    }

    /// <summary>
    ///     Gets the required permissions (at least one must be present).
    /// </summary>
    public string[] Permissions { get; }
}
