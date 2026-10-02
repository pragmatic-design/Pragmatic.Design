using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Emits <c>_Infra.Identity.AuthorizationRegistration.g.cs</c> — a per-assembly DI registration
///     extension that feeds the generated <c>PermissionRegistry.All</c>/<c>RoleRegistry.All</c> into the
///     authorization catalog. Each <c>Pragmatic.Authorization.PermissionInfo</c>/<c>RoleInfo</c>
///     is registered as an individual singleton, so <c>DefaultPermissionCatalog</c> (which injects
///     <c>IEnumerable&lt;PermissionInfo&gt;</c>/<c>IEnumerable&lt;RoleInfo&gt;</c>) aggregates the entries
///     of every assembly that declares permissions/roles. Invoked by the host via the Authorization
///     metadata aggregation (see <c>PragmaticHostTemplate.Startup</c>).
/// </summary>
internal sealed class AuthorizationRegistrationTemplate : CSharpTemplate
{
    private readonly bool _hasPermissions;
    private readonly bool _hasRoles;
    private readonly string _namespacePrefix;

    public AuthorizationRegistrationTemplate(string namespacePrefix, bool hasPermissions, bool hasRoles)
    {
        _namespacePrefix = namespacePrefix;
        _hasPermissions = hasPermissions;
        _hasRoles = hasRoles;
    }

    /// <summary>
    ///     The namespace + method the host must call to register this assembly's catalog entries.
    ///     Kept in sync with <see cref="RenderFile"/> so the metadata registrationMethod resolves.
    /// </summary>
    public string RegistrationMethodFqn => FqnFor(_namespacePrefix);

    /// <summary>
    ///     The same FQN, for callers that have only the namespace prefix — the local-registration
    ///     channel a host-declared permission or role travels through has no template instance.
    /// </summary>
    public static string FqnFor(string namespacePrefix)
        => GeneratedRegistrationNames.AuthorizationFqn(NamespaceFor(namespacePrefix));

    private static string NamespaceFor(string namespacePrefix)
        => string.IsNullOrEmpty(namespacePrefix) ? "Pragmatic.Authorization" : namespacePrefix;

    private string RegistryNamespace => NamespaceFor(_namespacePrefix);

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Identity", "AuthorizationRegistration"),
        ToSourceText());

    protected override bool Validate() => _hasPermissions || _hasRoles;

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(RegistryNamespace);
        AppendLine();

        XmlSummary("Registers the generated permission/role registry entries into the authorization catalog.");
        AppendLine($"public static partial class {GeneratedRegistrationNames.AuthorizationClass}");
        AppendLine("{");
        IncreaseIndent();

        XmlSummary("Registers every generated PermissionInfo/RoleInfo as a singleton so the catalog aggregates them across assemblies.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");
        AppendLine(
            $"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {GeneratedRegistrationNames.AuthorizationMethod}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        AppendLine("{");
        IncreaseIndent();

        if (_hasPermissions)
        {
            AppendLine($"foreach (var permission in global::{RegistryNamespace}.PermissionRegistry.All)");
            IncreaseIndent();
            AppendLine("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, permission);");
            DecreaseIndent();
        }

        if (_hasRoles)
        {
            AppendLine($"foreach (var role in global::{RegistryNamespace}.RoleRegistry.All)");
            IncreaseIndent();
            AppendLine("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, role);");
            DecreaseIndent();
        }

        AppendLine("return services;");
        DecreaseIndent();
        AppendLine("}");

        DecreaseIndent();
        AppendLine("}");
    }
}
