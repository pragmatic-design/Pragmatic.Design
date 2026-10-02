using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates the permission constant classes, one per boundary: the CRUD permissions of its entities
///     nested per entity — <c>CatalogPermissions.Amenity.Create</c> — and the declared ones beside them.
///     Entities without a boundary get a standalone top-level class.
/// </summary>
/// <remarks>
///     <para>
///         The <b>one</b> class per boundary. A declared permission — <c>[assembly: Permission]</c>, a
///         <c>[RequirePermission(Description = …)]</c> — is a <c>const</c> here, under the entity its resource
///         names or a class of its own: <c>LeavePermissions.PersonalData.Erase</c>. Its path is
///         <c>PermissionNaming.FromValue</c>'s, the one the constant catalogue predicts. There is no second
///         class of the same name for the <c>IPermission</c> ones: <c>static readonly</c> members would be
///         unusable in an attribute.
///     </para>
///     <para>
///         A first segment with declared permissions and no entity still gets its class, with the wildcard and
///         the declared constants only — what the catalogue says it carries. In an assembly with no boundary,
///         a first segment that is an entity's own CRUD class (<c>team.archive</c>, <c>TeamPermissions</c>)
///         puts its constants in that class rather than a second one of the same name.
///     </para>
/// </remarks>
internal sealed class EntityPermissionsTemplate : CSharpTemplate
{
    private readonly CrudPermissionsAggregateModel _model;

    public EntityPermissionsTemplate(CrudPermissionsAggregateModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Identity", "EntityPermissions"),
        ToSourceText());

    protected override bool Validate() => _model.Entities.Length > 0 || _model.Declared.Count > 0;

    public override void RenderFile()
    {
        AppendNamespace(_model.RootNamespace);
        AppendLine();

        var declaredBySlug = _model.Declared
            .GroupBy(d => d.Value.Split('.')[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var entitiesBySlug = _model.Entities
            .Where(e => !string.IsNullOrEmpty(e.BoundarySlug))
            .GroupBy(e => e.BoundarySlug!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.EntityName).ToList(), StringComparer.OrdinalIgnoreCase);

        var standalone = _model.Entities
            .Where(e => string.IsNullOrEmpty(e.BoundarySlug))
            .OrderBy(e => e.EntityName)
            .ToList();
        var standaloneClasses = new HashSet<string>(
            standalone.Select(e => e.EntityName + PermissionNaming.ClassSuffix), StringComparer.Ordinal);

        // A first segment whose class is a standalone entity's joins that class, below.
        var slugs = entitiesBySlug.Keys
            .Concat(declaredBySlug.Keys.Where(s => !standaloneClasses.Contains(PermissionNaming.ClassNameFor(s))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (var slug in slugs)
        {
            RenderBoundaryPermissions(
                slug,
                entitiesBySlug.TryGetValue(slug, out var entities) ? entities : [],
                declaredBySlug.TryGetValue(slug, out var declared) ? declared : []);
            AppendLine();
        }

        // Entities without boundary → standalone top-level classes
        foreach (var entity in standalone)
        {
            var className = entity.EntityName + PermissionNaming.ClassSuffix;
            var declared = declaredBySlug
                .Where(g => PermissionNaming.ClassNameFor(g.Key) == className)
                .SelectMany(g => g.Value)
                .Select(d => (Segments: d.Value.Split('.'), Declared: d))
                .ToList();
            RenderStandaloneEntityPermissions(entity, declared);
            AppendLine();
        }
    }

    private void RenderBoundaryPermissions(
        string slug, List<EntityCrudPermissionModel> entities, List<DeclaredPermissionConstantModel> declared)
    {
        var className = PermissionNaming.ClassNameFor(slug);
        var items = declared.Select(d => (Segments: d.Value.Split('.'), Declared: d)).ToList();

        XmlSummary($"Permissions for the {slug} boundary. Nested classes per entity for discoverability.");
        Class(className, () =>
        {
            // A value with no resource is the boundary's own, and the catalogue calls it Resource.
            if (entities.Count > 0 || items.Any(i => i.Segments.Length == 1))
            {
                XmlSummary("L0: access to this boundary.");
                AppendLine($"public const string Resource = \"{slug}\";");
                AppendLine();
            }

            XmlSummary("Wildcard: all permissions in this boundary.");
            AppendLine($"public const string All = \"{slug}.*\";");

            RenderDeclaredMembers(items, depth: 1);

            var nested = items.Where(i => i.Segments.Length > 2)
                .GroupBy(i => PermissionNaming.ToPascalCase(i.Segments[1]), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var entity in entities)
            {
                AppendLine();
                RenderNestedEntityPermissions(entity,
                    nested.TryGetValue(entity.EntityName, out var under) ? under : []);
                nested.Remove(entity.EntityName);
            }

            foreach (var group in nested.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                AppendLine();
                RenderDeclaredClass(group.Key, group.Value, depth: 2);
            }
        }, modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderNestedEntityPermissions(
        EntityCrudPermissionModel entity, List<(string[] Segments, DeclaredPermissionConstantModel Declared)> declared)
    {
        var prefix = entity.PermissionPrefix;

        XmlSummary($"CRUD permissions for {entity.EntityName}.");
        Class(entity.EntityName, () =>
        {
            RenderCrudConstants(prefix);
            RenderDeclaredMembers(declared, depth: 2);
            RenderDeclaredClasses(declared, depth: 2);
        }, modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    /// <summary>A class holding only declared permissions, for a resource that is not an entity.</summary>
    private void RenderDeclaredClass(
        string name, List<(string[] Segments, DeclaredPermissionConstantModel Declared)> declared, int depth)
    {
        XmlSummary($"Permissions for {name}.");
        Class(name, () =>
        {
            RenderDeclaredMembers(declared, depth);
            RenderDeclaredClasses(declared, depth);
        }, modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    /// <summary>The constants whose last segment sits at <paramref name="depth" />.</summary>
    private void RenderDeclaredMembers(
        List<(string[] Segments, DeclaredPermissionConstantModel Declared)> declared, int depth)
    {
        foreach (var item in declared.Where(i => i.Segments.Length == depth + 1).OrderBy(i => i.Declared.Value, StringComparer.Ordinal))
        {
            var member = PermissionNaming.MemberFor(item.Segments[depth]);
            if (member is null)
                continue;

            AppendLine();
            XmlSummary(item.Declared.Description is { Length: > 0 } description
                ? description
                : $"Declared permission <c>{item.Declared.Value}</c>.");
            AppendLine($"public const string {member} = \"{StringHelper.CSharpLiteral(item.Declared.Value)}\";");
        }
    }

    /// <summary>The classes for the segments below <paramref name="depth" />.</summary>
    private void RenderDeclaredClasses(
        List<(string[] Segments, DeclaredPermissionConstantModel Declared)> declared, int depth)
    {
        foreach (var group in declared.Where(i => i.Segments.Length > depth + 1)
                     .GroupBy(i => PermissionNaming.ToPascalCase(i.Segments[depth]), StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            AppendLine();
            RenderDeclaredClass(group.Key, group.ToList(), depth + 1);
        }
    }

    private void RenderStandaloneEntityPermissions(
        EntityCrudPermissionModel entity, List<(string[] Segments, DeclaredPermissionConstantModel Declared)> declared)
    {
        var className = entity.EntityName + PermissionNaming.ClassSuffix;
        var prefix = entity.PermissionPrefix;

        XmlSummary($"CRUD permissions for {entity.EntityName} (no boundary).");
        Class(className, () =>
        {
            RenderCrudConstants(prefix);
            RenderDeclaredMembers(declared, depth: 1);
            RenderDeclaredClasses(declared, depth: 1);
        }, modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    /// <summary>
    ///     The four operations, for every entity.
    /// </summary>
    /// <remarks>
    ///     <c>Delete</c> is not gated on <c>[SoftDelete]</c>, which would have it exactly backwards: a
    ///     hard delete is the one that cannot be undone, and it would be the one with no constant to guard
    ///     it. A mutation naming <c>Permissions.Thing.Delete</c> on such an entity would not compile — and
    ///     writing the string by hand instead trips <c>PRAG0418</c>, because nothing generated it.
    ///     Deleting is an operation any entity can have; whether the row survives is a
    ///     persistence detail and no business of the permission catalogue.
    /// </remarks>
    private void RenderCrudConstants(string prefix)
    {
        XmlSummary("L0: access to this resource.");
        AppendLine($"public const string Resource = \"{prefix}\";");
        AppendLine();

        XmlSummary("L1: read access.");
        AppendLine($"public const string Read = \"{prefix}.read\";");
        AppendLine();

        XmlSummary("L1: create access.");
        AppendLine($"public const string Create = \"{prefix}.create\";");
        AppendLine();

        XmlSummary("L1: update access.");
        AppendLine($"public const string Update = \"{prefix}.update\";");

        AppendLine();
        XmlSummary("L1: delete access.");
        AppendLine($"public const string Delete = \"{prefix}.delete\";");

        AppendLine();
        XmlSummary("Bypass: read every row, past the ownership and scope filters.");
        AppendLine($"public const string ViewAll = \"{prefix}.{Core.PermissionNaming.ViewAllVerb}\";");

        AppendLine();
        XmlSummary("Wildcard: all operations on this entity.");
        AppendLine($"public const string All = \"{prefix}.*\";");
    }
}
