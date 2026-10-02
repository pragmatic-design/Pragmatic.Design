using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Identity.Diagnostics;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     The one <c>{Boundary}Permissions</c> class per boundary: the CRUD permissions of the entities, and every
///     declared permission — <c>[assembly: Permission]</c>, <c>[RequirePermission(Description = …)]</c> — as a
///     <c>const</c> beside them.
/// </summary>
/// <remarks>
///     <para>
///         Registered after Identity, not inside Persistence: the declared permissions are Identity's to read,
///         and the class has to hold them too. Its inputs arrive through the pipeline, as every cross-feature
///         fact in this generator does.
///     </para>
///     <para>
///         Checked here, where every source meets: a declared value whose first segment is no boundary of the
///         assembly (PRAG1004), a path already taken — by a CRUD constant or another declaration (PRAG1001) — and
///         a name its class already gives to a class or a constant (PRAG1005). Each would otherwise be CS0102 or
///         CS0542 in a file the author cannot open.
///     </para>
///     <para>
///         An assembly with no boundary at all — a package such as <c>Pragmatic.Authorization.Management</c> —
///         has no boundary class to put a constant in, and nothing to check a first segment against: there the
///         first segment names the class (<c>authorization.view</c> → <c>AuthorizationPermissions.View</c>), and
///         when that class is an entity's own CRUD class the constant joins it.
///     </para>
/// </remarks>
internal static class PermissionsClassFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<EntityMetadataModel>> entities,
        IncrementalValueProvider<EquatableArray<DeclaredPermissionModel>> declared)
    {
        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "");
        var input = entities.Combine(features).Combine(declared).Combine(assemblyName);

        context.RegisterSourceOutputSafe(input, static (ctx, data) =>
        {
            var (((all, detected), customs), assembly) = data;
            if (!detected.HasAuthorization)
                return;

            List<EntityMetadataModel> local = detected.HasPersistenceEFCore
                ? all.Where(e => e.IsValid && e is { IsFromReference: false, IsAbstract: false }).ToList()
                : [];
            if (local.Count == 0 && customs.IsDefaultOrEmpty)
                return;

            var crud = local
                .Select(e => new EntityCrudPermissionModel
                {
                    TypeName = e.TypeName,
                    Accessibility = "public",
                    TypeKind = "class",
                    EntityName = e.TypeName,
                    Namespace = e.Namespace,
                    BoundarySlug = e.BoundaryName?.ToLowerInvariant(),
                    BoundaryName = e.BoundaryName
                })
                .ToImmutableArray();

            var boundarySlugs = crud
                .Where(e => e.BoundarySlug is not null)
                .Select(e => e.BoundarySlug!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToImmutableArray();

            var constants = Checked(ctx, crud, boundarySlugs, customs);

            var model = new CrudPermissionsAggregateModel
            {
                TypeName = "EntityPermissions",
                Accessibility = "public",
                TypeKind = "class",
                Entities = crud,
                BoundarySlugs = boundarySlugs,
                RootNamespace = RootNamespace(local, assembly),
                Declared = constants
            };

            var artifact = new EntityPermissionsTemplate(model).RenderOutput();
            if (!artifact.IsEmpty)
                ctx.AddSource(artifact);
        });
    }

    /// <summary>The declared constants that can be emitted — each outside-the-boundaries and taken path reported.</summary>
    private static ImmutableArray<DeclaredPermissionConstantModel> Checked(
        SourceProductionContext ctx,
        ImmutableArray<EntityCrudPermissionModel> crud,
        ImmutableArray<string> boundarySlugs,
        EquatableArray<DeclaredPermissionModel> customs)
    {
        // Every constant the class already carries, every class it nests, and who put it there.
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in crud)
        {
            var by = $"the CRUD permissions of {entity.EntityName}";
            foreach (var (member, _) in PermissionNaming.EntityCrud)
                taken[PermissionNaming.ForEntityMember(entity.BoundarySlug, entity.TypeName, member)] = by;
            taken[PermissionNaming.ForEntityMember(entity.BoundarySlug, entity.TypeName, PermissionNaming.EntityDelete.Member)] = by;
            if (entity.BoundarySlug is not null)
                classes[PermissionNaming.ClassNameFor(entity.BoundarySlug) + "." + entity.TypeName] = $"the entity {entity.EntityName}";
        }

        foreach (var slug in boundarySlugs)
        foreach (var (member, _) in PermissionNaming.BoundaryMembers)
            taken[PermissionNaming.ClassNameFor(slug) + "." + member] = $"the {slug} boundary";

        var constants = ImmutableArray.CreateBuilder<DeclaredPermissionConstantModel>();

        foreach (var custom in customs)
        {
            var slug = custom.Value.Split('.')[0];
            var path = PermissionNaming.FromValue(custom.Value);
            var outside = !boundarySlugs.IsEmpty && !boundarySlugs.Contains(slug, StringComparer.Ordinal);
            if (path is null || outside)
            {
                Report(ctx, IdentityDiagnostics.PermissionOutsideTheBoundaries, custom.Location,
                    custom.Value, custom.Source,
                    boundarySlugs.IsEmpty ? "any — the assembly has no boundary, but the value is empty" : string.Join(", ", boundarySlugs));
                continue;
            }

            if (taken.TryGetValue(path, out var owner))
            {
                Report(ctx, IdentityDiagnostics.DuplicatePermissionName, custom.Location, custom.Value, owner, custom.Source);
                continue;
            }

            if (NameClash(path, taken, classes) is { } clash)
            {
                Report(ctx, IdentityDiagnostics.PermissionConstantNameTaken, custom.Location, custom.Value, custom.Source, clash.Name, clash.By);
                continue;
            }

            Take(path, custom.Source, taken, classes);
            constants.Add(new DeclaredPermissionConstantModel(custom.Value, custom.Description));
        }

        return constants.ToImmutable();
    }

    /// <summary>
    ///     The name a constant at <paramref name="path" /> would take from something already in the class: the
    ///     constant named like a nested class, one of its classes named like a constant, or either named like the
    ///     class enclosing it.
    /// </summary>
    private static (string Name, string By)? NameClash(
        string path, Dictionary<string, string> taken, Dictionary<string, string> classes)
    {
        if (classes.TryGetValue(path, out var nestedBy))
            return (path, nestedBy);

        var segments = path.Split('.');
        for (var i = 1; i < segments.Length; i++)
        {
            var name = string.Join(".", segments, 0, i + 1);
            if (segments[i] == segments[i - 1])
                return (name, "the class enclosing it");
            if (i < segments.Length - 1 && taken.TryGetValue(name, out var member))
                return (name, member);
        }

        return null;
    }

    private static void Take(
        string path, string by, Dictionary<string, string> taken, Dictionary<string, string> classes)
    {
        taken[path] = by;
        var segments = path.Split('.');
        for (var i = 1; i < segments.Length - 1; i++)
        {
            var name = string.Join(".", segments, 0, i + 1);
            if (!classes.ContainsKey(name))
                classes[name] = by;
        }
    }

    /// <summary>The namespace of the class: the entities' root when there are entities, as before; otherwise the assembly's name.</summary>
    private static string RootNamespace(List<EntityMetadataModel> local, string assembly)
    {
        if (local.Count > 0)
        {
            // The top two segments of the first entity's namespace ("Showcase.Catalog" from
            // "Showcase.Catalog.Entities"), unchanged: every caller names the class there.
            var ns = local[0].Namespace;
            var dot = ns.IndexOf('.');
            if (dot <= 0)
                return ns;
            var second = ns.IndexOf('.', dot + 1);
            return second > 0 ? ns.Substring(0, second) : ns;
        }

        return NamespacePrefixHelper.ToIdentifier(assembly);
    }

    private static void Report(
        SourceProductionContext ctx, DiagnosticDescriptor descriptor, LocationInfo? at, params object[] args)
        => ctx.ReportDiagnostic(Diagnostic.Create(descriptor, at?.ToLocation() ?? Location.None, args));
}
