using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     Resolves <c>[RequirePermission(GeneratedConst)]</c> references against the entity-permission
///     catalog. A source generator cannot resolve the constants it generates itself in the same
///     compilation, so these references are captured from syntax during the transform and resolved here
///     (after the catalog is available) — otherwise the permission would silently not be enforced.
/// </summary>
internal static partial class ActionsFeature
{
    internal static ImmutableArray<ActionModel> ResolveActionPermissions(
        ImmutableArray<ActionModel> actions, EquatableArray<PermissionConstEntry> catalog)
    {
        if (actions.IsDefaultOrEmpty)
            return actions;

        var dict = BuildCatalogDict(catalog);
        var builder = ImmutableArray.CreateBuilder<ActionModel>(actions.Length);
        foreach (var a in actions)
        {
            if (!a.HasUnresolvedPermissionPaths)
            {
                builder.Add(a);
                continue;
            }

            builder.Add(a with
            {
                RequireAllPermissions = MergeResolved(a.RequireAllPermissions, a.UnresolvedRequireAllPaths, dict),
                RequireAnyPermissions = MergeResolved(a.RequireAnyPermissions, a.UnresolvedRequireAnyPaths, dict)
            });
        }

        return builder.ToImmutable();
    }

    internal static ImmutableArray<MutationModel> ResolveMutationPermissions(
        ImmutableArray<MutationModel> mutations, EquatableArray<PermissionConstEntry> catalog)
    {
        if (mutations.IsDefaultOrEmpty)
            return mutations;

        var dict = BuildCatalogDict(catalog);
        var builder = ImmutableArray.CreateBuilder<MutationModel>(mutations.Length);
        foreach (var m in mutations)
        {
            if (!m.HasUnresolvedPermissionPaths)
            {
                builder.Add(m);
                continue;
            }

            builder.Add(m with
            {
                RequireAllPermissions = MergeResolved(m.RequireAllPermissions, m.UnresolvedRequireAllPaths, dict),
                RequireAnyPermissions = MergeResolved(m.RequireAnyPermissions, m.UnresolvedRequireAnyPaths, dict)
            });
        }

        return builder.ToImmutable();
    }

    private static Dictionary<string, string> BuildCatalogDict(EquatableArray<PermissionConstEntry> catalog)
        => PermissionCatalogLookup.Index(catalog);

    private static EquatableArray<string> MergeResolved(
        EquatableArray<string> resolved, EquatableArray<string> unresolvedPaths, Dictionary<string, string> dict)
    {
        if (unresolvedPaths.IsDefaultOrEmpty)
            return resolved;

        var builder = ImmutableArray.CreateBuilder<string>();
        builder.AddRange(resolved.AsImmutableArray());
        foreach (var path in unresolvedPaths)
        {
            var value = ResolvePath(path, dict);
            if (value is not null && !builder.Contains(value))
                builder.Add(value);
        }

        return new EquatableArray<string>(builder.ToImmutable());
    }

    private static string? ResolvePath(string path, Dictionary<string, string> dict)
        => PermissionCatalogLookup.Resolve(path, dict);

    /// <summary>
    ///     Reports PRAG0418 for any <c>[RequirePermission]</c> constant reference that could not be
    ///     resolved against the catalog — such a permission would not be enforced (fail-open), so surface
    ///     it rather than let it pass silently.
    /// </summary>
    private static void ReportUnresolvedActionPermissions(
        SourceProductionContext context, (ImmutableArray<ActionModel> Actions, EquatableArray<PermissionConstEntry> Catalog) input)
    {
        var dict = BuildCatalogDict(input.Catalog);
        foreach (var a in input.Actions)
        {
            if (!a.HasUnresolvedPermissionPaths)
                continue;
            ReportUnresolved(context, a.TypeName, a.Location, a.UnresolvedRequireAllPaths, dict);
            ReportUnresolved(context, a.TypeName, a.Location, a.UnresolvedRequireAnyPaths, dict);
        }
    }

    private static void ReportUnresolvedMutationPermissions(
        SourceProductionContext context, (ImmutableArray<MutationModel> Mutations, EquatableArray<PermissionConstEntry> Catalog) input)
    {
        var dict = BuildCatalogDict(input.Catalog);
        foreach (var m in input.Mutations)
        {
            if (!m.HasUnresolvedPermissionPaths)
                continue;
            ReportUnresolved(context, m.TypeName, m.Location, m.UnresolvedRequireAllPaths, dict);
            ReportUnresolved(context, m.TypeName, m.Location, m.UnresolvedRequireAnyPaths, dict);
        }
    }

    /// <summary>
    ///     Reports PRAG0422 for every <c>[RequirePermission]</c> / <c>[RequireAnyPermission]</c>
    ///     applied with no permission. Nothing downstream can catch this — the type never enters the
    ///     requirement registry, so at runtime it is indistinguishable from an action that declared no
    ///     requirement, and the pipeline lets it through.
    /// </summary>
    private static void ReportEmptyPermissionRequirements(
        SourceProductionContext context,
        (ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) input)
    {
        foreach (var a in input.Actions)
            ReportEmptyPermissions(context, a.TypeName, a.Location, a.EmptyPermissionAttributes);

        foreach (var m in input.Mutations)
            ReportEmptyPermissions(context, m.TypeName, m.Location, m.EmptyPermissionAttributes);
    }

    private static void ReportEmptyPermissions(
        SourceProductionContext context, string typeName, Location? location,
        EquatableArray<string> attributeNames)
    {
        if (attributeNames.IsDefaultOrEmpty)
            return;

        foreach (var attributeName in attributeNames)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.EmptyPermissionRequirement, location, typeName, attributeName));
        }
    }

    private static void ReportUnresolved(
        SourceProductionContext context, string typeName, Location? location,
        EquatableArray<string> paths, Dictionary<string, string> dict)
    {
        foreach (var path in paths)
        {
            if (ResolvePath(path, dict) is null)
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.PermissionConstNotResolved, location, typeName, path));
        }
    }
}
