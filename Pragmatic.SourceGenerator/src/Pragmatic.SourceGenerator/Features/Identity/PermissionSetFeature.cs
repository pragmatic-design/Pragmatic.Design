using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Features.Identity.Diagnostics;
using Pragmatic.SourceGenerator.Features.Identity.Models;
using Pragmatic.SourceGenerator.Features.Identity.Templates;
using Pragmatic.SourceGenerator.Features.Identity.Transforms;

namespace Pragmatic.SourceGenerator.Features.Identity;

/// <summary>
///     <c>[PermissionSet]</c>: this assembly states what a list of permissions holds, for the compilations
///     that cannot read it.
/// </summary>
/// <remarks>
///     <para>
///         Its own feature rather than a stage of <see cref="IdentityFeature" />: a published list is a
///         fact about this assembly, true on its own, which is what makes it readable from outside:
///         the module declares, whoever reads the list composes.
///     </para>
///     <para>
///         ⚠️ It does take the permission catalogue, and that is not a contradiction: the catalogue is
///         this run's own map from constant path to value, for the constants this run writes. A list
///         naming <c>BookingPermissions.Reservation.Read</c> is still a fact about this assembly — it
///         simply cannot be spelled out until the value exists, and the value is ours. Without it the
///         attribute would report PRAG1016 on every list built from generated constants, which is every
///         list a module actually writes.
///     </para>
/// </remarks>
internal static class PermissionSetFeature
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<EquatableArray<Core.PermissionConstEntry>> permissionCatalog)
    {
        var sets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PermissionSetTransform.PermissionSetAttribute,
                static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
                PermissionSetTransform.Transform)
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect();

        context.RegisterSourceOutputSafe(sets.Combine(permissionCatalog), static (ctx, input) =>
        {
            var (all, catalog) = input;
            if (all.IsDefaultOrEmpty)
                return;

            var index = PermissionCatalogLookup.Index(catalog);
            var resolved = all.Select(set => Resolve(set, index)).ToImmutableArray();

            foreach (var unreadable in resolved.Where(static set => set.CannotBeRead))
                ctx.ReportDiagnostic(Diagnostic.Create(
                    IdentityDiagnostics.PermissionSetCannotBeRead,
                    unreadable.Location?.ToLocation() ?? Location.None, unreadable.MemberFqn));

            var published = resolved.Where(static set => !set.CannotBeRead).ToImmutableArray();
            if (published.IsEmpty)
                return;

            var artifact = new PermissionSetValuesTemplate(published).RenderOutput();
            if (!artifact.IsEmpty)
                ctx.AddSource(artifact);
        });
    }

    /// <summary>
    ///     The list with its constant paths spelled out, and the verdict on whether anything is left to
    ///     publish.
    /// </summary>
    /// <remarks>
    ///     A path with no entry in the catalogue is dropped rather than invented — the same rule the
    ///     role side follows. A list that ends up empty is the one PRAG1016 is for: marked, and this
    ///     assembly has nothing to state about it.
    /// </remarks>
    private static PermissionSetModel Resolve(
        PermissionSetModel set, System.Collections.Generic.Dictionary<string, string> index)
    {
        if (set.CannotBeRead)
            return set;

        var values = ImmutableArray.CreateBuilder<string>();
        values.AddRange(set.Values.AsImmutableArray());

        foreach (var path in set.UnresolvedPaths)
            if (PermissionCatalogLookup.Resolve(path, index) is { } value && !values.Contains(value))
                values.Add(value);

        return values.Count == 0
            ? set with { CannotBeRead = true }
            : set with { Values = new EquatableArray<string>(values.ToImmutable()) };
    }
}
