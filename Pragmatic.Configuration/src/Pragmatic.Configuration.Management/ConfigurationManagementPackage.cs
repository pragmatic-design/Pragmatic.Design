using Pragmatic.Composition;

namespace Pragmatic.Configuration.Management;

/// <summary>
///     Admin management package for configuration.
///     Import via <c>[UsePackage&lt;ConfigurationManagementPackage&gt;]</c> to expose
///     configuration CRUD and audit log endpoints under /admin/configuration.
/// </summary>
public sealed class ConfigurationManagementPackage : IPackageDefinition
{
    /// <inheritdoc />
    public static string PackageName => "Pragmatic.Configuration.Management";

    /// <inheritdoc />
    public static string? RoutePrefix => "admin/configuration";

    /// <inheritdoc />
    public static string? Description => "Configuration admin: view, edit, delete config values + audit log";
}
