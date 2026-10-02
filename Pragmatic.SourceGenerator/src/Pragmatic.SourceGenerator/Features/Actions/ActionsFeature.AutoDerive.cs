using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     Opt-in auto-derivation of action/mutation permissions. An operation that carries no
///     <c>[RequirePermission]</c> today requires nothing; with the switch on it requires
///     <c>{boundary}.{operationname}</c> — the same shape
///     <see cref="PermissionNaming.ValueForEntityMember" /> gives entity CRUD, so a boundary's derived
///     and generated names read as one vocabulary.
/// </summary>
/// <remarks>
///     <para>
///         <b>Off by default, and off must mean untouched.</b> Turning it on derives a permission for
///         every operation that carries none, and each of them is then denied until a role grants a
///         name that exists only once the switch is on. So every entry point here returns its input
///         unchanged when the switch is off — not a rebuilt copy, the same array — and the stages
///         downstream see exactly what they would see without this feature.
///     </para>
///     <para>
///         The derived name is written into <c>RequireAllPermissions</c>, so it travels through the
///         registry, the metadata and the runtime filter by the same path a hand-written attribute does.
///         There is no second enforcement mechanism to keep in step.
///     </para>
/// </remarks>
internal static partial class ActionsFeature
{
    internal const string AutoDeriveBuildProperty = "PragmaticAutoDerivePermissions";
    internal const string AutoDeriveAttributeName = "Pragmatic.Authorization.PragmaticAutoDerivePermissionsAttribute";

    /// <summary>Source label written into the manifest for a name this feature invented.</summary>
    internal const string SourceAutoDerived = "auto-derived";

    /// <summary>Source label for a name <c>[ExplicitPermission]</c> supplied.</summary>
    internal const string SourceExplicit = "explicit";

    // "Query" too: a [Query] is an operation of the boundary like the other two, and the permission
    // names the operation, not the class that spells it.
    private static readonly string[] OperationSuffixes = ["Mutation", "Action", "Query"];

    /// <summary>
    ///     Whether this compilation opts in, by build property or by assembly attribute. Both, combined
    ///     with <c>||</c>, for the reason <c>SerializationFeature.Register</c> gives: the attribute makes
    ///     the feature testable and survives a project reference that never imports the package's
    ///     <c>.props</c>.
    /// </summary>
    internal static IncrementalValueProvider<bool> AutoDeriveEnabled(
        IncrementalGeneratorInitializationContext context)
    {
        var fromProperty = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            provider.GlobalOptions.TryGetValue($"build_property.{AutoDeriveBuildProperty}", out var value)
            && value.Equals("true", StringComparison.OrdinalIgnoreCase));

        var fromAttribute = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.Assembly.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == AutoDeriveAttributeName));

        return fromProperty.Combine(fromAttribute).Select(static (p, _) => p.Left || p.Right);
    }

    // =========================================================================
    // Derivation
    // =========================================================================

    internal static ImmutableArray<ActionModel> DeriveActionPermissions(
        ImmutableArray<ActionModel> actions,
        ImmutableArray<BoundaryModel> boundaries,
        EquatableArray<PermissionConstEntry> catalog,
        bool enabled)
    {
        if (!enabled || actions.IsDefaultOrEmpty)
            return actions;

        var index = PermissionCatalogLookup.Index(catalog);
        var builder = ImmutableArray.CreateBuilder<ActionModel>(actions.Length);

        foreach (var action in actions)
        {
            var resolved = ResolvePermission(
                action.IsValid, action.AllowAnonymous, action.HasPermissionRequirement,
                action.HasUnresolvedPermissionPaths, action.ExplicitPermission,
                action.TypeName, action.Namespace, action.BelongsToTypeName, boundaries, index);

            builder.Add(resolved is null
                ? action
                : action with
                {
                    RequireAllPermissions = new EquatableArray<string>(ImmutableArray.Create(resolved.Value.Name)),
                    PermissionSource = resolved.Value.Source
                });
        }

        return builder.ToImmutable();
    }

    internal static ImmutableArray<MutationModel> DeriveMutationPermissions(
        ImmutableArray<MutationModel> mutations,
        ImmutableArray<BoundaryModel> boundaries,
        EquatableArray<PermissionConstEntry> catalog,
        bool enabled)
    {
        if (!enabled || mutations.IsDefaultOrEmpty)
            return mutations;

        var index = PermissionCatalogLookup.Index(catalog);
        var builder = ImmutableArray.CreateBuilder<MutationModel>(mutations.Length);

        foreach (var mutation in mutations)
        {
            var resolved = ResolvePermission(
                mutation.IsValid, mutation.AllowAnonymous, mutation.HasPermissionRequirement,
                mutation.HasUnresolvedPermissionPaths, mutation.ExplicitPermission,
                mutation.TypeName, mutation.Namespace, mutation.BelongsToTypeName, boundaries, index);

            builder.Add(resolved is null
                ? mutation
                : mutation with
                {
                    RequireAllPermissions = new EquatableArray<string>(ImmutableArray.Create(resolved.Value.Name)),
                    PermissionSource = resolved.Value.Source
                });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The permission an operation ends up requiring, or <c>null</c> to leave it exactly as it is.
    ///     Null covers all three reasons for not touching it: invalid model, opted out, or already
    ///     carrying a requirement somebody wrote by hand.
    /// </summary>
    internal static (string Name, string Source)? ResolvePermission(
        bool isValid,
        bool allowAnonymous,
        bool hasPermissionRequirement,
        bool hasUnresolvedPaths,
        ExplicitPermissionModel? explicitPermission,
        string typeName,
        string ns,
        string? belongsToTypeName,
        ImmutableArray<BoundaryModel> boundaries,
        Dictionary<string, string> catalogIndex)
    {
        if (!isValid || allowAnonymous)
            return null;

        // A hand-written requirement wins over anything derived — including one whose constant path has
        // not been resolved yet: that path is the requirement, and overwriting it would silently swap an
        // author's choice for a guess.
        if (hasPermissionRequirement || hasUnresolvedPaths)
            return null;

        var explicitName = ResolveExplicit(explicitPermission, catalogIndex);
        if (explicitName is not null)
            return (explicitName, SourceExplicit);

        return (DeriveName(typeName, ns, belongsToTypeName, boundaries), SourceAutoDerived);
    }

    private static string? ResolveExplicit(
        ExplicitPermissionModel? explicitPermission, Dictionary<string, string> catalogIndex)
    {
        if (explicitPermission is null)
            return null;

        if (!string.IsNullOrEmpty(explicitPermission.Value))
            return explicitPermission.Value;

        if (!string.IsNullOrEmpty(explicitPermission.ConstPath))
            return PermissionCatalogLookup.Resolve(explicitPermission.ConstPath!, catalogIndex);

        return null;
    }

    /// <summary>
    ///     The permission value for an operation nobody gave one. Three segments when the name opens with
    ///     a recognised verb — <c>AddGuestCommentAction</c> becomes <c>booking.guest-comment.add</c>, the
    ///     shape a hand-written <c>catalog.property.read</c> already has — and the flat two-segment form
    ///     otherwise: <c>IssueRefundMutation</c> in the Billing boundary → <c>billing.issue-refund</c>.
    ///     The value is built by <see cref="PermissionNaming.ValueForEntityMember" />, the same call the
    ///     entity CRUD constants go through, so <c>billing.invoice</c> and <c>billing.issue-refund</c>
    ///     cannot drift into two different conventions — kebab-case included.
    /// </summary>
    /// <remarks>
    ///     One vocabulary rather than two: with two, a role file mixing declared and derived permissions
    ///     would mix <c>catalog.property.read</c> with <c>booking.addguestcomment</c>, and the second is
    ///     hard to read precisely where it matters, in the list of what a role grants.
    /// </remarks>
    internal static string DeriveName(
        string typeName, string ns, string? belongsToTypeName, ImmutableArray<BoundaryModel> boundaries)
    {
        var slug = BoundarySlugFor(ns, belongsToTypeName, boundaries);
        var operation = StripOperationSuffix(typeName);

        return PermissionNaming.TrySplitLeadingVerb(operation, out var resource, out var verb)
            ? PermissionNaming.ValueForEntityMember(slug, resource, verb)
            : PermissionNaming.ValueForEntityMember(slug, operation, verb: null);
    }

    /// <summary>
    ///     The boundary an operation belongs to, as a lowercase slug, or <c>null</c> when it belongs to
    ///     none. Resolution follows the same two rules <c>MatchMembersToBoundary</c> uses — an explicit
    ///     <c>[BelongsTo]</c> first, then the longest namespace prefix — so the permission prefix and the
    ///     generated boundary interface never disagree about which boundary an action is in.
    /// </summary>
    private static string? BoundarySlugFor(
        string ns, string? belongsToTypeName, ImmutableArray<BoundaryModel> boundaries)
    {
        if (belongsToTypeName is not null)
        {
            foreach (var boundary in boundaries)
            {
                if (boundary.FullTypeName == belongsToTypeName)
                    return SlugFor(boundary.TypeName);
            }

            // [BelongsTo<TBoundary>] pointing at a boundary declared in a referenced assembly: the
            // collected boundaries are this compilation's only. The type name still carries the slug.
            return SlugFor(SimpleNameOf(belongsToTypeName));
        }

        var match = FindBestBoundaryMatch(ns, boundaries);
        return match is null ? null : SlugFor(match.TypeName);
    }

    private static string? SlugFor(string boundaryTypeName)
    {
        var stripped = StripBoundarySuffix(boundaryTypeName);
        return string.IsNullOrEmpty(stripped) ? null : stripped.ToLowerInvariant();
    }

    private static string SimpleNameOf(string fullyQualifiedName)
    {
        var bare = StripGlobalPrefix(fullyQualifiedName);
        var lastDot = bare.LastIndexOf('.');
        return lastDot >= 0 ? bare.Substring(lastDot + 1) : bare;
    }

    private static string StripGlobalPrefix(string value)
        => value.StartsWith("global::", StringComparison.Ordinal) ? value.Substring("global::".Length) : value;

    /// <summary>
    ///     Drops the taxonomy suffix, so <c>IssueRefundMutation</c> and <c>IssueRefundAction</c> both
    ///     derive <c>issue-refund</c> — the permission names an operation, not the class that spells it.
    /// </summary>
    internal static string StripOperationSuffix(string typeName)
    {
        foreach (var suffix in OperationSuffixes)
        {
            if (typeName.Length > suffix.Length && typeName.EndsWith(suffix, StringComparison.Ordinal))
                return typeName.Substring(0, typeName.Length - suffix.Length);
        }

        return typeName;
    }

    // =========================================================================
    // Seeding
    // =========================================================================

    /// <summary>
    ///     Registers the catalog of names this assembly's operations acquired from the switch, so a role
    ///     can grant them, and returns the host-local registration for it.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    /// <param name="derived">
    ///     Every name the switch produced in this compilation — actions and mutations from
    ///     <see cref="CollectDerivedPermissions" />, queries from the Endpoints feature. One catalog for
    ///     all of them: a second file for the reads would be a second registration method, a second
    ///     metadata attribute, and a second place for the two to disagree.
    /// </param>
    /// <remarks>
    ///     Called from the entry point rather than from <c>Register</c>, because the reads are derived
    ///     by a feature that runs <i>after</i> Actions and needs what Actions returns. With the switch
    ///     off <paramref name="derived" /> is empty — nothing sets a source otherwise — and neither the
    ///     file nor the registration is produced.
    /// </remarks>
    internal static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>>
        RegisterDerivedPermissionCatalog(
            IncrementalGeneratorInitializationContext context,
            IncrementalValueProvider<EquatableArray<DerivedPermissionEntry>> derived)
    {
        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "");
        var hasComposition = context.CompilationProvider
            .Select(static (c, _) => CompositionDetector.IsCompositionReferenced(c));
        var input = derived.Combine(assemblyName.Combine(hasComposition));

        context.RegisterSourceOutputSafe(input, GenerateActionPermissionCatalog);

        // A host that derived its own permissions has a catalog to register, and no
        // [assembly: PragmaticMetadata] of its own to learn it from.
        return input.Select(static (p, _) =>
        {
            var (entries, (name, composition)) = p;
            if (!composition || entries.IsDefaultOrEmpty)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return new EquatableArray<Composition.Models.MetadataEntry>(ImmutableArray.Create(
                Composition.Models.HostLocalRegistration.Create(
                    MetadataCategoryIds.Authorization,
                    MetadataSchemaVersions.Authorization,
                    Core.GeneratedRegistrationNames.ActionPermissionCatalogFqn(name))));
        });
    }

    /// <summary>
    ///     Emits the catalog of names this assembly's operations acquired from the switch. Only the
    ///     operations the derivation stages actually touched are listed: a source is set nowhere else,
    ///     so with the switch off the list is empty and the template renders nothing.
    /// </summary>
    private static void GenerateActionPermissionCatalog(
        SourceProductionContext context,
        (EquatableArray<DerivedPermissionEntry> Derived, (string AssemblyName, bool HasComposition) Info) input)
    {
        var (derived, (assemblyName, hasComposition)) = input;
        if (derived.IsDefaultOrEmpty)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<Templates.ActionPermissionCatalogTemplate.PermissionEntry>();

        foreach (var entry in derived)
        {
            if (!seen.Add(entry.Name))
                continue;

            var dot = entry.Name.IndexOf('.');
            entries.Add(new Templates.ActionPermissionCatalogTemplate.PermissionEntry(
                entry.Name,
                $"Required by {SimpleNameOf(entry.OperationTypeFqn)} ({entry.Source}).",
                dot > 0 ? entry.Name.Substring(0, dot) : null));
        }

        var catalog = new Templates.ActionPermissionCatalogTemplate(entries.ToImmutable(), assemblyName);
        context.AddSource(catalog.RenderOutput());

        // A referenced boundary library reaches the host only through metadata; without Composition
        // there is no host reading it, and the attribute type would not resolve.
        if (!hasComposition)
            return;

        context.AddSource(new Templates.ActionPermissionCatalogMetadataTemplate(
            catalog.RegistrationMethodFqn, entries.Count).RenderOutput());
    }

    /// <summary>
    ///     The names the switch produced, keyed by operation type, for the manifest. Empty with the
    ///     switch off — <see cref="ActionModel.PermissionSource" /> is set by the derivation stage alone.
    /// </summary>
    internal static EquatableArray<DerivedPermissionEntry> CollectDerivedPermissions(
        ImmutableArray<ActionModel> actions, ImmutableArray<MutationModel> mutations)
    {
        var builder = ImmutableArray.CreateBuilder<DerivedPermissionEntry>();

        void Add(string? source, EquatableArray<string> permissions, string fullTypeName)
        {
            if (source is null || permissions.IsDefaultOrEmpty)
                return;
            foreach (var name in permissions)
                builder.Add(new DerivedPermissionEntry(StripGlobalPrefix(fullTypeName), name, source));
        }

        if (!actions.IsDefaultOrEmpty)
            foreach (var action in actions)
                Add(action.PermissionSource, action.RequireAllPermissions, action.FullTypeName);

        if (!mutations.IsDefaultOrEmpty)
            foreach (var mutation in mutations)
                Add(mutation.PermissionSource, mutation.RequireAllPermissions, mutation.FullTypeName);

        return new EquatableArray<DerivedPermissionEntry>(builder.ToImmutable());
    }

    // =========================================================================
    // Diagnostics
    // =========================================================================

    /// <summary>
    ///     Reports PRAG0421 for an <c>[ExplicitPermission(SomeConstant)]</c> whose constant is not in the
    ///     catalog — one no generator writes in this compilation. The operation still gets the derived name
    ///     (fail-closed), but silently honouring the wrong name is exactly the failure the catalog exists to
    ///     prevent, so it is surfaced.
    /// </summary>
    private static void ReportUnresolvedExplicitPermissions(
        SourceProductionContext context,
        ((ImmutableArray<ActionModel> Actions, ImmutableArray<MutationModel> Mutations) Models,
            (EquatableArray<PermissionConstEntry> Catalog, bool Enabled) Resolution) input)
    {
        var ((actions, mutations), (catalog, enabled)) = input;
        if (!enabled)
            return;

        var index = PermissionCatalogLookup.Index(catalog);

        foreach (var action in actions)
            ReportIfUnresolved(context, action.ExplicitPermission, action.TypeName, action.Location, index);

        foreach (var mutation in mutations)
            ReportIfUnresolved(context, mutation.ExplicitPermission, mutation.TypeName, mutation.Location, index);
    }

    internal static void ReportIfUnresolved(
        SourceProductionContext context,
        ExplicitPermissionModel? explicitPermission,
        string typeName,
        Location? location,
        Dictionary<string, string> index)
    {
        if (explicitPermission is null)
            return;
        if (ResolveExplicit(explicitPermission, index) is not null)
            return;

        var reference = explicitPermission.ConstPath ?? "";
        context.ReportDiagnostic(Diagnostic.Create(
            ActionsDiagnostics.ExplicitPermissionNotResolved, location, typeName, reference));
    }
}
