using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using Pragmatic.SourceGenerator.Features.Actions.Transforms;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     Boundary pipeline: diagnostics, interface generation, metadata, and namespace matching helpers.
/// </summary>
internal static partial class ActionsFeature
{
    private static void ReportBoundaryDiagnostics(SourceProductionContext context, BoundaryModel model)
    {
        // PRAG0431 is not an invalidity: the boundary is fine, the attribute on it is inert.
        if (model.DeclaresTransactional && model.Location is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.TransactionalOnBoundary, model.Location, model.TypeName));
        }

        if (model.IsValid || model.Location is null)
            return;

        switch (model.InvalidReason)
        {
            // NotPartial: PRAG0406 is the companion analyzer's.

            case BoundaryInvalidReason.NoNamespace:
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.BoundaryNoNamespace, model.Location, model.TypeName));
                break;
        }
    }

    private static void GenerateBoundaryInterfaces(
        SourceProductionContext context,
        ((((ImmutableArray<BoundaryModel> Boundaries, ImmutableArray<ActionModel> Actions) Left,
            ImmutableArray<MutationModel> Mutations) MembersLeft,
            ImmutableArray<Persistence.Models.QueryModel> Queries) WithQueries,
            Compilation Compilation) input)
    {
        var (boundaries, actions) = input.WithQueries.MembersLeft.Left;
        var mutations = input.WithQueries.MembersLeft.Mutations;
        var queries = input.WithQueries.Queries;
        var compilation = input.Compilation;

        if (boundaries.IsEmpty)
            return;

        // Read package action registrations from referenced assemblies (for [UsePackage<T>] on modules)
        var packageRegistrationsByBoundaryNs = ReadPackageRegistrations(compilation);

        // Convert actions to boundary members (filter out system actions)
        var actionMembers = actions
            .Where(a => !a.IsSystem)
            .Select(BoundaryMemberModel.FromAction)
            .ToList();

        // Convert mutations to boundary members
        var mutationMembers = mutations
            .Select(BoundaryMemberModel.FromMutation)
            .ToList();

        // Convert declared queries to boundary members. A read is an operation: it belongs on the
        // facade beside the writes, or the module next door builds the query object, finds a source and
        // picks an executor overload by hand — which is what every application was doing, which is to
        // say none of them were.
        var queryMembers = queries
            .Where(static q => q.IsValid && q.IsPartial)
            .Select(BoundaryMemberModel.FromQuery)
            .ToList();

        var allMembers = new List<BoundaryMemberModel>(
            actionMembers.Count + mutationMembers.Count + queryMembers.Count);
        allMembers.AddRange(actionMembers);
        allMembers.AddRange(mutationMembers);
        allMembers.AddRange(queryMembers);

        foreach (var boundary in boundaries)
        {
            var allMatched = RefuseUnusableDeclaredGroups(
                context, boundary, MatchMembersToBoundary(boundary, allMembers, boundaries));

            // Group members into sub-boundaries (if any sub-namespace structure exists)
            var (boundaryLevelMembers, subBoundaries) =
                GroupMembersIntoSubBoundaries(boundary, allMatched);

            // Surface the sub-boundary structure (PRAG0413 inferred) and warn on nesting deeper
            // than 2 levels (PRAG0412).
            ReportSubBoundaryDiagnostics(context, boundary, allMatched, subBoundaries);

            // ⚠️ Both halves come from what the grouping returned, and that is the whole point of it.
            // Re-deriving the internal one from `allMatched` would put a member the grouping has just
            // filed under a sub-boundary on the root as well: one operation declared twice, on the
            // group's internal twin and flat here. A template test cannot see it — a test that
            // supplies the model itself supplies the answer.
            var publicMembers = boundaryLevelMembers.Where(m => !m.IsInternal).ToImmutableArray();
            var internalMembers = boundaryLevelMembers.Where(m => m.IsInternal).ToImmutableArray();

            // Package action registrations for this boundary's module
            var import = packageRegistrationsByBoundaryNs.TryGetValue(boundary.Namespace, out var found)
                ? found
                : null;
            var pkgRegs = import?.Registrations ?? ImmutableArray<PackageActionRegistration>.Empty;

            // The boundary the module named at the import, and whether it may be used. A package's
            // invoker asks for DbContext/IUnitOfWork unkeyed because no boundary existed where it was
            // generated; the importer is the only place that can answer, and this is where it does.
            var importedBoundary = ResolveImportedBoundary(context, boundary, import, pkgRegs);

            // Create sub-boundary models for package actions
            var packageSubBoundaries = CreatePackageSubBoundaries(boundary, pkgRegs);
            var allSubBoundaries = subBoundaries.AddRange(packageSubBoundaries);

            if (publicMembers.IsEmpty && internalMembers.IsEmpty && allSubBoundaries.IsEmpty && pkgRegs.IsEmpty)
                continue;

            var enrichedBoundary = boundary with
            {
                SubBoundaries = allSubBoundaries,
                PackageBoundaryKey = importedBoundary
            };

            // Generate separate files: Definition (interfaces + DI switch), Local, Remote
            // Remote is skipped when HttpClient isn't available (e.g., test contexts without ASP.NET Core)
            var hasHttpTypes = compilation.GetTypeByMetadataName("System.Net.Http.HttpClient") is not null;
            var modes = hasHttpTypes
                ? new[] { BoundaryOutputMode.Definition, BoundaryOutputMode.Local, BoundaryOutputMode.Remote }
                : new[] { BoundaryOutputMode.Definition, BoundaryOutputMode.Local };

            foreach (var mode in modes)
            {
                var artifact = new BoundaryInterfaceTemplate(enrichedBoundary, publicMembers, internalMembers, pkgRegs, mode, hasHttpTypes)
                    .RenderOutput();
                if (!artifact.IsEmpty)
                    context.AddSource(artifact);
            }
        }
    }

    private static void GenerateBoundaryModuleMetadata(
        SourceProductionContext context,
        (BoundaryModel Boundary, (bool IsDebug, bool HasComposition) Info) input)
    {
        var (boundary, (_, hasComposition)) = input;

        if (!hasComposition)
            return;

        var artifact = new BoundaryModuleMetadataTemplate(boundary).RenderOutput();
        context.AddSource(artifact);
    }

    // =========================================================================
    // Sub-boundary grouping
    // =========================================================================

    /// <summary>
    ///     Emits the sub-boundary diagnostics: PRAG0413 (info) noting a sub-boundary was inferred
    ///     from the namespace, and PRAG0412 (warning) when nesting exceeds two levels. Reported once per
    ///     distinct sub-path, in deterministic order, on the first operation (by full type name) whose
    ///     namespace produced it — one line per group rather than one per member, because a group of
    ///     five mutations is one fact, not five.
    /// </summary>
    private static void ReportSubBoundaryDiagnostics(
        SourceProductionContext context, BoundaryModel boundary,
        ImmutableArray<BoundaryMemberModel> allMatched, ImmutableArray<SubBoundaryModel> subBoundaries)
    {
        if (subBoundaries.IsDefaultOrEmpty)
            return;

        // Which sub-paths were inferred from the namespace (vs an explicit [SubBoundary]/SubBoundaryName),
        // and where the operation that produced each one is declared.
        var inferredAt = new Dictionary<string, Location?>(StringComparer.Ordinal);
        foreach (var member in allMatched.OrderBy(m => m.FullTypeName, StringComparer.Ordinal))
        {
            if (member.SubBoundaryName is not null)
                continue;
            var inferredPath = SubBoundaryTransform.InferSubBoundary(boundary.Namespace, member.Namespace);
            if (inferredPath is not null && !inferredAt.ContainsKey(inferredPath))
                inferredAt[inferredPath] = member.LocationInfo?.ToLocation();
        }

        foreach (var sub in subBoundaries.OrderBy(s => s.FullPath, StringComparer.Ordinal))
        {
            inferredAt.TryGetValue(sub.FullPath, out var location);

            var depth = sub.FullPath.Split('.').Length;
            if (depth > 2)
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.SubBoundaryNestingTooDeep, location,
                    sub.FullPath, boundary.TypeName, depth));

            if (inferredAt.ContainsKey(sub.FullPath))
                context.ReportDiagnostic(Diagnostic.Create(
                    ActionsDiagnostics.SubBoundaryInferred, location,
                    sub.FullPath, boundary.TypeName));
        }
    }

    /// <summary>
    ///     PRAG0416: a declared group name that cannot be one is reported, and dropped.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Here rather than in the transform, because the rule needs the boundary: a group named
    ///         after it would generate <c>ISalesSalesActions</c>, and a group is never named after its
    ///         module, whether inferred or declared — it is a rule about the group.
    ///     </para>
    ///     <para>
    ///         ⚠️ The member is not dropped, only its declared name: the operation goes where it would
    ///         have gone without the attribute. Dropping the operation would answer a bad group name
    ///         by removing a call from the boundary.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<BoundaryMemberModel> RefuseUnusableDeclaredGroups(
        SourceProductionContext context, BoundaryModel boundary, ImmutableArray<BoundaryMemberModel> allMatched)
    {
        var shortName = StripBoundarySuffix(boundary.TypeName);
        ImmutableArray<BoundaryMemberModel>.Builder? corrected = null;

        for (var i = 0; i < allMatched.Length; i++)
        {
            var member = allMatched[i];
            var declared = member.DeclaredSubBoundaryName;

            var problem = declared switch
            {
                null => null,
                _ when string.IsNullOrWhiteSpace(declared) =>
                    "a group needs a name. Without one the namespace decides, which is what the "
                    + "attribute is written to override",
                _ when string.Equals(declared.Trim(), shortName, StringComparison.OrdinalIgnoreCase) =>
                    $"a group named after its own boundary separates nothing — it would publish "
                    + $"I{shortName}{shortName}Actions",
                _ => null,
            };

            if (problem is null)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.SubBoundaryNameCannotBeAGroup, member.LocationInfo?.ToLocation(),
                declared, member.TypeName, problem));

            corrected ??= allMatched.ToBuilder();
            corrected[i] = member with { SubBoundaryName = null, DeclaredSubBoundaryName = null };
        }

        return corrected?.ToImmutable() ?? allMatched;
    }

    private static (ImmutableArray<BoundaryMemberModel> BoundaryLevel, ImmutableArray<SubBoundaryModel> SubBoundaries)
        GroupMembersIntoSubBoundaries(BoundaryModel boundary, ImmutableArray<BoundaryMemberModel> allMatched)
    {
        var boundaryLevel = ImmutableArray.CreateBuilder<BoundaryMemberModel>();
        var subGroups = new Dictionary<string, List<BoundaryMemberModel>>(StringComparer.Ordinal);
        var subInternalGroups = new Dictionary<string, List<BoundaryMemberModel>>(StringComparer.Ordinal);

        foreach (var member in allMatched)
        {
            // Explicit SubBoundaryName takes precedence (from traits/resource); otherwise the namespace
            // decides. [BelongsTo] on the operation names the boundary and nothing else. If it
            // cancelled the inferred group as well, an operation filed in Amenities/ next to the
            // mutations that form the group would land on the root — silently, when its route is
            // declared by hand and no end-to-end case can see the difference.
            var subPath = member.SubBoundaryName
                ?? SubBoundaryTransform.InferSubBoundary(boundary.Namespace, member.Namespace);
            if (subPath is null)
            {
                boundaryLevel.Add(member);
            }
            else
            {
                // The group holds both halves. An internal member goes on the group's internal twin,
                // which is the seam a caller inside the module holds. Put flat onto the root's internal
                // interface whatever its namespace said, the path a caller writes would depend on the
                // operation's visibility — `knowledge.Candidates.Discard(...)` next to
                // `knowledge.Settle(...)`, for two operations in the same directory.
                var target = member.IsInternal ? subInternalGroups : subGroups;

                if (!target.TryGetValue(subPath, out var list))
                {
                    list = new List<BoundaryMemberModel>();
                    target[subPath] = list;
                }

                list.Add(member);
            }
        }

        if (subGroups.Count == 0 && subInternalGroups.Count == 0)
            return (boundaryLevel.ToImmutable(), ImmutableArray<SubBoundaryModel>.Empty);

        var shortBoundaryName = StripBoundarySuffix(boundary.TypeName);
        var subBuilder = ImmutableArray.CreateBuilder<SubBoundaryModel>();

        // ⚠️ Both dictionaries, because a group can be made entirely of internal operations. Iterating
        // the public one alone would drop such a group without a word, and its members with it.
        var subPaths = new List<string>(subGroups.Keys);
        foreach (var path in subInternalGroups.Keys)
            if (!subGroups.ContainsKey(path))
                subPaths.Add(path);

        foreach (var subPath in subPaths)
        {
            var members = subGroups.TryGetValue(subPath, out var pub) ? pub : new List<BoundaryMemberModel>();
            var internalMembers = subInternalGroups.TryGetValue(subPath, out var inner)
                ? inner
                : new List<BoundaryMemberModel>();

            if (members.Count == 0 && internalMembers.Count == 0)
                continue;

            // "Guests" → "Guests", "Properties.Photos" → "PropertiesPhotos"
            var concatenatedName = subPath.Replace(".", "");
            var propertyName = subPath.Contains(".")
                ? subPath.Split('.')[0] // Top-level property name
                : subPath;

            subBuilder.Add(new SubBoundaryModel
            {
                Name = concatenatedName,
                FullPath = subPath,
                InterfaceName = $"I{shortBoundaryName}{concatenatedName}Actions",
                ImplementationName = $"{shortBoundaryName}{concatenatedName}LocalActions",
                PropertyName = propertyName == concatenatedName ? propertyName : concatenatedName,
                // The first operation of the group that wrote one, in the order they were matched.
                // A group is one interface and gets one summary; the alternative — refusing two
                // different descriptions — would be a diagnostic about prose.
                Description = members.Concat(internalMembers)
                    .Select(m => m.SubBoundaryDescription)
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)),
                PublicMembers = members.ToImmutableArray(),
                InternalMembers = internalMembers.ToImmutableArray()
            });
        }

        return (boundaryLevel.ToImmutable(), subBuilder.ToImmutable());
    }

    private static string StripBoundarySuffix(string typeName)
    {
        const string suffix = "Boundary";
        return typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;
    }

    // =========================================================================
    // Matching helpers
    // =========================================================================

    private static ImmutableArray<BoundaryMemberModel> MatchMembersToBoundary(
        BoundaryModel boundary,
        List<BoundaryMemberModel> members,
        ImmutableArray<BoundaryModel> allBoundaries)
    {
        var builder = ImmutableArray.CreateBuilder<BoundaryMemberModel>();

        foreach (var member in members)
        {
            if (member.BelongsToTypeName is not null)
            {
                if (member.BelongsToTypeName == boundary.FullTypeName)
                    builder.Add(member);
            }
            else
            {
                var bestMatch = FindBestBoundaryMatch(member.Namespace, allBoundaries);
                if (bestMatch is not null && bestMatch.FullTypeName == boundary.FullTypeName)
                    builder.Add(member);
            }
        }

        return builder.ToImmutable();
    }

    private static BoundaryModel? FindBestBoundaryMatch(
        string actionNamespace,
        ImmutableArray<BoundaryModel> boundaries)
    {
        BoundaryModel? best = null;
        var bestLength = -1;

        foreach (var b in boundaries)
        {
            // Direct match: action namespace starts with boundary namespace
            if (IsNamespacePrefixOf(b.Namespace, actionNamespace))
            {
                if (b.Namespace.Length > bestLength)
                {
                    best = b;
                    bestLength = b.Namespace.Length;
                }

                continue;
            }

            // Sibling match: strip last segment from boundary namespace and try parent
            // e.g., boundary at Showcase.Booking.Actions → parent Showcase.Booking
            //        captures Showcase.Booking.Mutations, Showcase.Booking.Dtos, etc.
            var parentNs = GetParentNamespace(b.Namespace);
            if (parentNs is not null && IsNamespacePrefixOf(parentNs, actionNamespace))
            {
                if (parentNs.Length > bestLength)
                {
                    best = b;
                    bestLength = parentNs.Length;
                }
            }
        }

        return best;
    }

    private static bool IsNamespacePrefixOf(string prefix, string ns)
        => ns.StartsWith(prefix, StringComparison.Ordinal) &&
           (ns.Length == prefix.Length || ns[prefix.Length] == '.');

    private static string? GetParentNamespace(string ns)
    {
        var lastDot = ns.LastIndexOf('.');
        return lastDot > 0 ? ns.Substring(0, lastDot) : null;
    }

    /// <summary>
    ///     The boundary a module named when importing packages, once it is known to be usable.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Returns <c>null</c> when no imported operation needs a boundary-keyed service — which is
    ///         every package that does not touch persistence, and both of the ones this framework
    ///         ships. Nothing is emitted in that case and nothing has to be declared.
    ///     </para>
    ///     <para>
    ///         ⚠️ Two diagnostics live here rather than in the template, because a template that renders
    ///         nothing is indistinguishable from one that was never asked to render.
    ///     </para>
    /// </remarks>
    private static string? ResolveImportedBoundary(
        SourceProductionContext context,
        BoundaryModel boundary,
        PackageImport? import,
        ImmutableArray<PackageActionRegistration> registrations)
    {
        if (import is null || registrations.IsDefaultOrEmpty)
            return null;

        var needy = registrations
            .Where(r => !r.BoundaryKeyedServices.IsDefaultOrEmpty)
            .ToList();

        if (needy.Count == 0)
            return null;

        // Each package that needs one must have been given one at its own import.
        var unanswered = needy
            .Where(r => r.SourceAssembly is null
                        || !import.BoundaryByPackageAssembly.ContainsKey(r.SourceAssembly))
            .ToList();

        if (unanswered.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.PackageNeedsABoundaryAtTheImport,
                import.Location?.ToLocation(),
                unanswered[0].ActionType,
                string.Join(", ", unanswered[0].BoundaryKeyedServices),
                boundary.TypeName));
            return null;
        }

        var answers = needy
            .Select(r => import.BoundaryByPackageAssembly[r.SourceAssembly!])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (answers.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.PackageImportsNameDifferentBoundaries,
                import.Location?.ToLocation(),
                string.Join(", ", answers)));
            return null;
        }

        return answers[0];
    }
}
