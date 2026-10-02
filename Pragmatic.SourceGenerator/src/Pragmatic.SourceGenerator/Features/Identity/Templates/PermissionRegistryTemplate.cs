using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates PermissionRegistry and RoleRegistry with all known permissions and roles.
/// </summary>
internal sealed class PermissionRegistryTemplate : CSharpTemplate
{
    private readonly ImmutableArray<PermissionModel> _permissions;
    private readonly ImmutableArray<RoleModel> _roles;
    private readonly string _namespacePrefix;

    public PermissionRegistryTemplate(
        ImmutableArray<PermissionModel> permissions,
        ImmutableArray<RoleModel> roles,
        string namespacePrefix)
    {
        _permissions = permissions;
        _roles = roles;
        _namespacePrefix = namespacePrefix;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Identity", "PermissionRegistry"),
        ToSourceText());

    protected override bool Validate() => _permissions.Length > 0 || _roles.Length > 0;

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");
        AddUsing("Pragmatic.Authorization");

        var ns = !string.IsNullOrEmpty(_namespacePrefix) ? _namespacePrefix : "Pragmatic.Authorization";
        AppendNamespace(ns);
        AppendLine();

        if (_permissions.Length > 0)
            RenderPermissionRegistry();

        if (_roles.Length > 0)
            RenderRoleRegistry();
    }

    private void RenderPermissionRegistry()
    {
        XmlSummary("Registry of all known permissions in this assembly.");
        Class("PermissionRegistry", () =>
        {
            XmlSummary("All registered permissions.");
            ExpressionProperty("All", "IReadOnlyList<PermissionInfo>",
                BuildPermissionListExpression(),
                isStatic: true);
        }, modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderRoleRegistry()
    {
        XmlSummary("Registry of all known roles in this assembly.");
        Class("RoleRegistry", () =>
        {
            XmlSummary("All registered roles.");
            ExpressionProperty("All", "IReadOnlyList<RoleInfo>",
                BuildRoleListExpression(),
                isStatic: true);
        }, modifiers: new ClassModifiers { IsStatic = true });
    }

    private string BuildPermissionListExpression()
    {
        var items = _permissions
            .Select(p =>
            {
                var desc = p.Description is not null ? $"\"{Escape(p.Description)}\"" : "null";
                var cat = p.Category is not null ? $"\"{Escape(p.Category)}\"" : "null";
                return $"new PermissionInfo(\"{Escape(p.Name)}\", {desc}, {cat})";
            });
        return $"[{string.Join(", ", items)}]";
    }

    private string BuildRoleListExpression()
    {
        var items = _roles
            .Select(r =>
            {
                var desc = r.Description is not null ? $"\"{Escape(r.Description)}\"" : "null";
                var perms = r.DefaultPermissions.Length > 0
                    ? $"[{string.Join(", ", r.DefaultPermissions.Select(p => $"\"{Escape(p)}\""))}]"
                    : "[]";
                return $"new RoleInfo(\"{Escape(r.Name)}\", {desc}, {perms})";
            });
        return $"[{string.Join(", ", items)}]";
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
