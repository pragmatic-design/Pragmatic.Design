using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Diagnostics;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Identity.Templates;

namespace Pragmatic.SourceGenerator.Features.Identity;

/// <summary>
///     Turns the <c>[Role]</c> classes into roles: each one's grants resolved through the permission catalogue
///     and flattened through the roles it includes — de-duplicated, ordered — ready for the generated members
///     and for the role registry, which list the same set.
/// </summary>
/// <remarks>
///     Here rather than in the transform because both halves of the answer arrive only at this level: a
///     granted constant is usually one this run generates, and an included role may be declared the same way.
///     Refused, each with its diagnostic rather than a silent drop: a class that cannot take the members
///     (PRAG1006), roles that include each other (PRAG1007), a grant no generator writes (PRAG1008) and a
///     hand-written role of another assembly (PRAG1009).
/// </remarks>
internal sealed class DeclaredRoleResolver
{
    private readonly SourceProductionContext _ctx;
    private readonly Dictionary<string, DeclaredRoleModel> _declared = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoleModel> _handWritten = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _catalog;
    private readonly Dictionary<string, ImmutableArray<string>> _flattened = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedCycles = new(StringComparer.Ordinal);

    private DeclaredRoleResolver(
        SourceProductionContext ctx,
        ImmutableArray<DeclaredRoleModel> declared,
        ImmutableArray<RoleModel> handWritten,
        EquatableArray<PermissionConstEntry> catalog)
    {
        _ctx = ctx;
        _catalog = PermissionCatalogLookup.Index(catalog);
        foreach (var role in declared)
            _declared[role.FullName] = role;
        foreach (var role in handWritten)
            _handWritten[role.SourceTypeFqn] = role;
    }

    /// <summary>
    ///     Writes the <c>IRole</c> members of each <c>[Role]</c> class and returns its registry entry, with the
    ///     same flattened permissions.
    /// </summary>
    public static IEnumerable<RoleModel> WithGeneratedMembers(
        SourceProductionContext ctx,
        ImmutableArray<DeclaredRoleModel> roleClasses,
        ImmutableArray<RoleModel> handWritten,
        EquatableArray<PermissionConstEntry> catalog)
    {
        foreach (var (role, permissions) in Resolve(ctx, roleClasses, handWritten, catalog))
        {
            SourceOutput.AddSource(ctx, new RoleMembersTemplate(role, permissions).RenderOutput());

            if (string.IsNullOrEmpty(role.Name))
                continue;

            yield return new RoleModel
            {
                Namespace = role.Namespace,
                TypeName = role.TypeName,
                Accessibility = "public",
                TypeKind = "class",
                Name = role.Name,
                Description = role.Description,
                DefaultPermissions = permissions,
                SourceTypeFqn = role.FullName
            };
        }
    }

    /// <summary>The roles whose members are generated, each with its flattened permissions.</summary>
    private static ImmutableArray<(DeclaredRoleModel Role, ImmutableArray<string> Permissions)> Resolve(
        SourceProductionContext ctx,
        ImmutableArray<DeclaredRoleModel> declared,
        ImmutableArray<RoleModel> handWritten,
        EquatableArray<PermissionConstEntry> catalog)
    {
        if (declared.IsDefaultOrEmpty)
            return ImmutableArray<(DeclaredRoleModel, ImmutableArray<string>)>.Empty;

        var resolver = new DeclaredRoleResolver(ctx, declared, handWritten, catalog);
        var roles = ImmutableArray.CreateBuilder<(DeclaredRoleModel, ImmutableArray<string>)>();

        foreach (var role in declared.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            if (!role.CanBeGenerated)
            {
                resolver.Report(IdentityDiagnostics.RoleMustBePartial, role, role.TypeName);
                continue;
            }

            foreach (var type in role.UnreadableIncludes)
                resolver.Report(IdentityDiagnostics.IncludedRoleCannotBeRead, role, role.TypeName, type);

            roles.Add((role, resolver.Flatten(role, [])));
        }

        return roles.ToImmutable();
    }

    /// <summary>The role's own grants plus every included role's, de-duplicated and ordered.</summary>
    private ImmutableArray<string> Flatten(DeclaredRoleModel role, List<string> chain)
    {
        if (_flattened.TryGetValue(role.FullName, out var done))
            return done;

        chain.Add(role.FullName);
        var permissions = new HashSet<string>(role.Grants, StringComparer.Ordinal);
        permissions.UnionWith(role.IncludedFromReferences);

        foreach (var path in role.GrantPaths)
        {
            if (PermissionCatalogLookup.Resolve(path, _catalog) is { } value)
                permissions.Add(value);
            else
                Report(IdentityDiagnostics.GrantedPermissionNotResolved, role, role.TypeName, path);
        }

        foreach (var include in role.Includes)
        {
            if (chain.Contains(include))
            {
                ReportCycle(role, chain, include);
                continue;
            }

            if (_declared.TryGetValue(include, out var declared))
                permissions.UnionWith(Flatten(declared, chain));
            else if (_handWritten.TryGetValue(include, out var handWritten))
                permissions.UnionWith(handWritten.DefaultPermissions);
        }

        chain.RemoveAt(chain.Count - 1);
        var result = permissions.OrderBy(p => p, StringComparer.Ordinal).ToImmutableArray();
        _flattened[role.FullName] = result;
        return result;
    }

    private void ReportCycle(DeclaredRoleModel role, List<string> chain, string include)
    {
        var loop = chain.Skip(chain.IndexOf(include)).Append(include).ToList();
        if (!_reportedCycles.Add(string.Join("|", loop.Skip(1).OrderBy(n => n, StringComparer.Ordinal))))
            return;

        Report(IdentityDiagnostics.RoleInclusionCycle, role, role.TypeName,
            string.Join(" → ", loop.Select(ShortName)));
    }

    private static string ShortName(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot < 0 ? fullName : fullName.Substring(dot + 1);
    }

    private void Report(DiagnosticDescriptor descriptor, DeclaredRoleModel role, params object[] args)
        => _ctx.ReportDiagnostic(Diagnostic.Create(descriptor, role.Location?.ToLocation() ?? Location.None, args));
}
