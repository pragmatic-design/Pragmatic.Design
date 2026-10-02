using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Emits the <c>PermissionInfo</c> entries for the permissions this assembly's actions and mutations
///     acquired by auto-derivation, plus the DI registration that feeds them into the same aggregated
///     authorization catalog <c>PermissionRegistry.All</c> flows into.
/// </summary>
/// <remarks>
///     Without this the feature could only ever deny: a derived name that appears in no catalog is a name
///     no role can be given, and no admin UI can offer. It is registered as individual singletons for the
///     same reason <c>AuthorizationRegistrationTemplate</c> does — <c>DefaultPermissionCatalog</c> injects
///     <c>IEnumerable&lt;PermissionInfo&gt;</c>, so every contributing assembly aggregates rather than
///     overwrites.
/// </remarks>
internal sealed class ActionPermissionCatalogTemplate : CSharpTemplate
{
    private readonly ImmutableArray<PermissionEntry> _entries;
    private readonly string _rootNamespace;

    public ActionPermissionCatalogTemplate(ImmutableArray<PermissionEntry> entries, string rootNamespace)
    {
        _entries = entries;
        _rootNamespace = rootNamespace;
    }

    /// <summary>The <c>{Namespace}.{Class}.{Method}</c> the host must call to register these entries.</summary>
    public string RegistrationMethodFqn => GeneratedRegistrationNames.ActionPermissionCatalogFqn(_rootNamespace);

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_entries.Length} auto-derived action permissions";
    protected override string? TriggerInfo => "[assembly: PragmaticAutoDerivePermissions]";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Actions", "ActionPermissionCatalog"),
        ToSourceText());

    protected override bool Validate() => _entries.Length > 0;

    public override void RenderFile()
    {
        AppendNamespace(GeneratedRegistrationNames.InGeneratedNamespace(_rootNamespace));
        AppendLine();

        XmlSummary("Permissions this assembly's actions and mutations acquired by auto-derivation.");
        AppendLine($"public static partial class {GeneratedRegistrationNames.ActionPermissionCatalogClass}");
        AppendLine("{");
        IncreaseIndent();

        RenderAll();
        AppendLine();
        RenderRegistration();

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderAll()
    {
        XmlSummary("Every auto-derived or [ExplicitPermission] name required in this assembly.");
        AppendLine(
            "public static global::System.Collections.Generic.IReadOnlyList<global::Pragmatic.Authorization.PermissionInfo> All =>");
        AppendLine("[");
        IncreaseIndent();

        foreach (var entry in _entries)
        {
            var category = entry.Category is null ? "null" : $"\"{Escape(entry.Category)}\"";
            AppendLine(
                $"new global::Pragmatic.Authorization.PermissionInfo(\"{Escape(entry.Name)}\", \"{Escape(entry.Description)}\", {category}),");
        }

        DecreaseIndent();
        AppendLine("];");
    }

    private void RenderRegistration()
    {
        XmlSummary("Registers each entry as a singleton so the catalog aggregates them across assemblies.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");
        AppendLine(
            $"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection {GeneratedRegistrationNames.ActionPermissionCatalogMethod}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("foreach (var permission in All)");
        IncreaseIndent();
        AppendLine("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, permission);");
        DecreaseIndent();
        AppendLine("return services;");
        DecreaseIndent();
        AppendLine("}");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <param name="Name">The permission value, e.g. <c>billing.issue-refund</c>.</param>
    /// <param name="Description">Which operation requires it, so an admin UI is not left guessing.</param>
    /// <param name="Category">The boundary slug, or null for an operation outside any boundary.</param>
    internal sealed record PermissionEntry(string Name, string Description, string? Category);
}
