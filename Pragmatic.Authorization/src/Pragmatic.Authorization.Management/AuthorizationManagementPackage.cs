using Pragmatic.Composition;

namespace Pragmatic.Authorization.Management;

/// <summary>
///     RBAC management package. Provides dynamic CRUD for permissions, roles, groups,
///     and user assignments, persisted by the engine of the boundary that imports it.
/// </summary>
public sealed class AuthorizationManagementPackage : IPackageDefinition
{
    /// <inheritdoc />
    public static string PackageName => "Pragmatic.Authorization.Management";

    /// <inheritdoc />
    public static string? RoutePrefix => "authorization";

    /// <inheritdoc />
    public static string? Description => "Dynamic RBAC management — permissions, roles, groups, and assignments";
}
