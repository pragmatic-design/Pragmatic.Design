using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Identity.Templates;

/// <summary>
///     Generates a static seeding extension method from roles.pragmatic.json.
///     Produces <c>SeedFromJson()</c> on <c>AuthorizationBuilder</c>.
///     Expands role inheritance at compile-time so the generated code has flattened permissions.
/// </summary>
internal sealed class RoleSeedingTemplate : CSharpTemplate
{
    private readonly RoleSeedingAggregateModel _model;

    public RoleSeedingTemplate(RoleSeedingAggregateModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Identity";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Identity", "RoleSeeding"),
        ToSourceText());

    protected override bool Validate()
        => !_model.Roles.IsDefaultOrEmpty || !_model.Groups.IsDefaultOrEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Authorization.Configuration");

        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary("Auto-generated role/group seeding from <c>roles.pragmatic.json</c>.");
        Class("RoleSeedingExtensions", () =>
        {
            XmlSummary("Seeds roles and groups from the compile-time JSON configuration.");
            XmlParam("builder", "The authorization builder.");
            XmlReturns("The builder for chaining.");

            Method("SeedFromJson", RenderSeedBody,
                "AuthorizationBuilder",
                new List<MethodParameter>
                {
                    new("AuthorizationBuilder", "builder") { IsExtension = true }
                },
                AccessModifier.Public,
                new MethodModifiers { IsStatic = true });
        }, modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderSeedBody()
    {
        // Expand inheritance at compile-time: each role gets its own + all inherited permissions
        var expandedPermissions = ExpandInheritance(_model.Roles.AsImmutableArray());

        // Roles
        if (!_model.Roles.IsDefaultOrEmpty)
        {
            foreach (var role in _model.Roles)
            {
                // Description as comment
                if (!string.IsNullOrEmpty(role.Description))
                    Comment(role.Description!);

                var effectivePerms = expandedPermissions.TryGetValue(role.Name, out var perms)
                    ? perms
                    : role.Permissions;

                if (effectivePerms.IsDefaultOrEmpty)
                {
                    AppendLine($"builder.MapRole(\"{Escape(role.Name)}\", _ => {{ }});");
                }
                else
                {
                    AppendLine($"builder.MapRole(\"{Escape(role.Name)}\", r => r");
                    IncreaseIndent();
                    var permStrings = string.Join(", ", effectivePerms.Select(p => $"\"{Escape(p)}\""));
                    AppendLine($".WithPermissions({permStrings}));");
                    DecreaseIndent();
                }
            }
        }

        if (!_model.Groups.IsDefaultOrEmpty)
        {
            AppendLine();
            foreach (var group in _model.Groups)
            {
                if (!string.IsNullOrEmpty(group.Description))
                    Comment(group.Description!);

                var roles = string.Join(", ", group.Roles.Select(r => $"\"{Escape(r)}\""));
                AppendLine($"builder.MapGroup(\"{Escape(group.Name)}\", g => g.WithRoles({roles}));");
            }
        }

        AppendLine();
        AppendLine("return builder;");
    }

    /// <summary>
    ///     Expands role inheritance at compile-time via recursive resolution.
    ///     Protects against circular references with a visited set.
    /// </summary>
    private static Dictionary<string, ImmutableArray<string>> ExpandInheritance(ImmutableArray<RoleSeedModel> roles)
    {
        var roleDict = roles.ToDictionary(r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
            Resolve(role.Name, roleDict, result, resolving);

        return result;
    }

    private static ImmutableArray<string> Resolve(
        string roleName,
        Dictionary<string, RoleSeedModel> roleDict,
        Dictionary<string, ImmutableArray<string>> resolved,
        HashSet<string> resolving)
    {
        if (resolved.TryGetValue(roleName, out var cached))
            return cached;

        if (!roleDict.TryGetValue(roleName, out var role))
            return ImmutableArray<string>.Empty;

        // Circular reference protection
        if (!resolving.Add(roleName))
            return role.Permissions.AsImmutableArray();

        var allPerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Own permissions first
        foreach (var p in role.Permissions)
            allPerms.Add(p);

        // Inherited permissions (recursive)
        foreach (var parentName in role.Inherits)
        {
            var parentPerms = Resolve(parentName, roleDict, resolved, resolving);
            foreach (var p in parentPerms)
                allPerms.Add(p);
        }

        resolving.Remove(roleName);
        var result = allPerms.ToImmutableArray();
        resolved[roleName] = result;
        return result;
    }

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
