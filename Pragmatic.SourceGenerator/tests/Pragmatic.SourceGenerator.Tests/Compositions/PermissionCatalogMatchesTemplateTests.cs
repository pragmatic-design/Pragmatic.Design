using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Compositions;

/// <summary>
///     The permission constants the generator <em>emits</em> and the catalogue the diagnostics
///     <em>consult</em> must describe the same thing.
/// </summary>
/// <remarks>
///     <para>
///         Two producers build the same string by two routes: <c>EntityPermissionsTemplate</c>
///         concatenates <c>"{prefix}.delete"</c> while <c>PermissionCatalogBuilder</c> calls
///         <c>PermissionNaming.ValueForEntityMember</c>. Both read from the same
///         <c>EntityMetadataModel</c>, and this test is what compares their output.
///     </para>
///     <para>
///         A source generator cannot bind a constant it is about to create, so
///         <c>[RequirePermission(BookingPermissions.Reservation.Delete)]</c> is resolved through the
///         catalogue; a constant the template emits and the catalogue omits is a permission reported as
///         unresolvable and therefore <b>not enforced</b>. Cataloguing <c>Delete</c> only for
///         soft-deletable entities while the template emits it for all is exactly that fail-open.
///     </para>
///     <para>
///         Deliberately a set comparison rather than a spot check on <c>Delete</c>: the defect is drift
///         between two rules, and a check on one named member would only prove that one member does not
///         drift.
///     </para>
/// </remarks>
public class PermissionCatalogMatchesTemplateTests
{
    /// <summary>
    ///     Entities chosen for the ways the two rules can disagree: with and without a boundary, and
    ///     multi-word names, where kebab-casing is a step one producer can skip.
    /// </summary>
    private static ImmutableArray<EntityMetadataModel> Entities() =>
    [
        Entity("Amenity", "catalog"),
        Entity("RoomType", "catalog"),
        Entity("CaseFile", "surveys"),
        Entity("Standalone", boundary: null)
    ];

    private static EntityMetadataModel Entity(string typeName, string? boundary) => new()
    {
        TypeName = typeName,
        FullTypeName = "App." + typeName,
        Namespace = "App",
        IdType = "System.Guid",
        BoundaryName = boundary,
        IsFromReference = false,
        IsAbstract = false,
        IsValid = true
    };

    /// <summary>The constants the template renders, as <c>Class.Nested.Member</c> → value.</summary>
    /// <remarks>
    ///     Read out of the generated text rather than out of the model, because the generated text is
    ///     what a consumer compiles against. Asserting on the model would compare the catalogue to the
    ///     template's input instead of to its output, which is the half that can drift.
    /// </remarks>
    private static Dictionary<string, string> Emitted()
    {
        var model = new CrudPermissionsAggregateModel
        {
            TypeName = "EntityPermissions",
            Accessibility = "public",
            TypeKind = "class",
            RootNamespace = "App",
            Entities =
            [
                .. Entities().Select(e => new EntityCrudPermissionModel
                {
                    TypeName = e.TypeName,
                    Accessibility = "public",
                    TypeKind = "class",
                    EntityName = e.TypeName,
                    Namespace = e.Namespace,
                    BoundarySlug = e.BoundaryName?.ToLowerInvariant(),
                    BoundaryName = e.BoundaryName
                })
            ],
            BoundarySlugs = [.. Entities().Select(e => e.BoundaryName?.ToLowerInvariant() ?? "").Distinct()]
        };

        return Parse(new EntityPermissionsTemplate(model).RenderOutput().Text);
    }

    /// <summary>
    ///     Rebuilds each constant's dotted path from the class nesting around it.
    /// </summary>
    /// <remarks>
    ///     Brace depth, not indentation: the path is what a caller writes, and a caller writes the
    ///     enclosing classes. Comments and XML doc carry no braces and no constants, so they need no
    ///     special handling.
    /// </remarks>
    private static Dictionary<string, string> Parse(string generated)
    {
        var classAt = new Dictionary<int, string>();
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        var depth = 0;
        string? pendingClass = null;

        foreach (var raw in generated.Split('\n'))
        {
            var line = raw.Trim();

            var declaration = Regex.Match(line, @"^public static partial class (\w+)$");
            if (declaration.Success)
            {
                pendingClass = declaration.Groups[1].Value;
                continue;
            }

            var constant = Regex.Match(line, @"^public const string (\w+) = ""(.*)"";$");
            if (constant.Success)
            {
                var path = string.Join(".",
                    Enumerable.Range(0, depth).Where(classAt.ContainsKey).Select(d => classAt[d]));
                found[path + "." + constant.Groups[1].Value] = constant.Groups[2].Value;
                continue;
            }

            foreach (var c in line)
                switch (c)
                {
                    case '{':
                        if (pendingClass is not null)
                        {
                            classAt[depth] = pendingClass;
                            pendingClass = null;
                        }
                        else
                        {
                            classAt.Remove(depth);
                        }

                        depth++;
                        break;
                    case '}':
                        depth--;
                        classAt.Remove(depth);
                        break;
                }
        }

        return found;
    }

    /// <summary>The catalogue's constant paths — what a <c>[RequirePermission]</c> argument can spell.</summary>
    /// <remarks>
    ///     Without the entries keyed by an entity (<c>PermissionNaming.EntityReadKey</c>), which name no
    ///     constant and are checked against the constants below instead.
    /// </remarks>
    private static Dictionary<string, string> Catalogued()
        => PermissionCatalogBuilder.Build(Entities(), [])
            .Where(e => !IsEntityReadEntry(e.ConstPath))
            .ToDictionary(e => e.ConstPath, e => e.Value, StringComparer.Ordinal);

    private static bool IsEntityReadEntry(string key)
        => Entities().Any(e => key == PermissionNaming.EntityReadKey(e.FullTypeName));

    [Fact]
    public void TheParserFindsTheConstantsAtAll()
        // Guards the comparison below: two empty sets are equal, and a parser that silently matched
        // nothing would make every assertion here pass for the wrong reason.
        => Emitted().Should().ContainKey("CatalogPermissions.Amenity.Read")
            .WhoseValue.Should().Be("catalog.amenity.read");

    [Fact]
    public void EveryEmittedConstant_IsInTheCatalogue()
    {
        // The direction that fails open: the constant compiles, the diagnostic cannot resolve it, and
        // the permission is reported as unenforceable.
        var missing = Emitted().Keys.Except(Catalogued().Keys).Order(StringComparer.Ordinal);

        missing.Should().BeEmpty();
    }

    [Fact]
    public void EveryCataloguedConstant_IsEmitted()
    {
        // The other direction: a catalogue entry with no constant behind it resolves a reference that
        // will not compile, which is loud rather than silent — but it means the two rules have parted.
        var extra = Catalogued().Keys.Except(Emitted().Keys).Order(StringComparer.Ordinal);

        extra.Should().BeEmpty();
    }

    [Fact]
    public void TheValuesAgree_NotJustTheNames()
    {
        // The failure that survives a name-only comparison: same constant, different string. A role
        // granted "catalog.casefile.read" does not satisfy a check for "catalog.case-file.read".
        var emitted = Emitted();

        var disagreeing = Catalogued()
            .Where(c => emitted.TryGetValue(c.Key, out var value) && value != c.Value)
            .Select(c => $"{c.Key}: emitted={emitted[c.Key]} catalogued={c.Value}")
            .Order(StringComparer.Ordinal);

        disagreeing.Should().BeEmpty();
    }

    /// <summary>
    ///     Each entity's read entry — what a preload's <c>RequireReadPermission</c> asks — is the value of the
    ///     entity's own <c>Read</c> constant: a check for another spelling is a 403 no grant can lift.
    /// </summary>
    [Fact]
    public void TheEntityReadEntry_IsTheReadConstantsValue()
    {
        var emitted = Emitted();
        var catalogue = PermissionCatalogBuilder.Build(Entities(), [])
            .ToDictionary(e => e.ConstPath, e => e.Value, StringComparer.Ordinal);

        catalogue[PermissionNaming.EntityReadKey("App.RoomType")]
            .Should().Be(emitted["CatalogPermissions.RoomType.Read"]);
        catalogue[PermissionNaming.EntityReadKey("App.CaseFile")]
            .Should().Be(emitted["SurveysPermissions.CaseFile.Read"]);
    }

    [Fact]
    public void DeleteIsThereForEveryEntity_WhateverItsPersistenceShape()
    {
        // The model carries no soft-delete flag, so this cannot be expressed as two cases. That is the
        // point: whether the row survives is a persistence detail, and a hard delete — the one that
        // cannot be undone — needs a constant to guard it as much as a soft one.
        var emitted = Emitted();

        emitted.Should().ContainKey("CatalogPermissions.Amenity.Delete");
        emitted.Should().ContainKey("CatalogPermissions.RoomType.Delete");
        emitted.Should().ContainKey("SurveysPermissions.CaseFile.Delete");
        emitted.Should().ContainKey("StandalonePermissions.Delete");
    }
}
