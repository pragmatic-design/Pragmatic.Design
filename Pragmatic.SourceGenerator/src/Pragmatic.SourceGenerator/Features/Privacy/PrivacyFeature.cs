using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Diagnostics;
using Pragmatic.SourceGenerator.Features.Privacy.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Templates;
using Pragmatic.SourceGenerator.Features.Privacy.Transforms;

namespace Pragmatic.SourceGenerator.Features.Privacy;

/// <summary>
///     Personal-data classification: builds the graph of entities reachable from a data subject and
///     reports what is missing or contradictory in their annotations.
/// </summary>
/// <remarks>
///     Nothing is reported until the compilation declares a <c>[DataSubject]</c>. That is deliberate and
///     load-bearing: PRAG2903 asks for a decision about every unclassified string, which across a
///     solution that has not opted in would be noise — and noise is how a real finding gets ignored.
/// </remarks>
internal static class PrivacyFeature
{
    /// <returns>
    ///     The privacy registration this compilation generates, for a host that declares its own
    ///     classified entities: the metadata attribute travels only to a host reading this assembly as a
    ///     <em>reference</em>, and cannot reach the compilation that emits it.
    /// </returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features,
        IncrementalValueProvider<ImmutableArray<EndpointModel>>? endpoints = null)
    {
        var subjects = TypesWith(context, AttributeNames.PrivacyDataSubject);
        var linked = TypesWith(context, AttributeNames.PrivacyLinksToSubject);
        var classified = TypesWithClassifiedProperties(context);

        // Optional so the feature stays testable on its own: without endpoints it reports everything
        // except PRAG2904, which has nothing to report against.
        var endpointModels = endpoints
            ?? context.CompilationProvider.Select(static (_, _) => ImmutableArray<EndpointModel>.Empty);

        var rootNamespace = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Global");

        var all = subjects.Collect()
            .Combine(linked.Collect())
            .Combine(classified.Collect())
            .Combine(features)
            .Combine(endpointModels)
            .Combine(rootNamespace);

        context.RegisterSourceOutputSafe(all, static (ctx, data) =>
        {
            var (((((fromSubjects, fromLinks), fromProperties), detected), endpointsForCheck), rootNs) = data;

            if (!detected.HasPrivacy)
                return;

            var entities = Deduplicate(fromSubjects, fromLinks, fromProperties);
            var reachable = SubjectGraphBuilder.Reachable(entities);

            ReportDiagnostics(ctx, entities, reachable);
            ReportExposures(ctx, entities, endpointsForCheck);

            // Only for entities a subject actually reaches. Generating an extractor for data nothing
            // can be erased from would suggest a capability that does not exist.
            foreach (var entity in reachable)
            {
                SourceOutput.AddSource(ctx, new PersonalDataExtractorTemplate(entity).RenderOutput());
                SourceOutput.AddSource(ctx, new ErasurePlanTemplate(entity).RenderOutput());
            }

            var adapters = GenerateAdapters(ctx, detected, entities, reachable);
            var activitySource = GenerateActivitySource(ctx, detected, reachable, endpointsForCheck, rootNs);
            var registration = GenerateRegistration(ctx, adapters, activitySource, rootNs);

            // One payload per assembly, for the host to assemble the Article 30 register from — and to
            // learn which generated Add* wires the code that acts on it.
            if (detected.HasComposition)
                SourceOutput.AddSource(
                    ctx, new PersonalDataMetadataTemplate(reachable, registration).RenderOutput());
        });

        return all.Select(static (data, _) =>
        {
            var (((((fromSubjects, fromLinks), fromProperties), detected), _), rootNs) = data;

            if (!detected.HasPrivacy || !detected.HasComposition)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            var entities = Deduplicate(fromSubjects, fromLinks, fromProperties);
            var reachable = SubjectGraphBuilder.Reachable(entities);

            // Mirrors the emission conditions above: telling the host to call a method that was not
            // generated is a build error, and not telling it about one that was is the disease this
            // whole layer exists to cure.
            var adapters = PlanAdapters(detected, entities, reachable);
            var activitySource = ActivitySourceFqn(detected, reachable, rootNs);

            if (adapters.Count == 0 && activitySource is null)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;

            return ImmutableArray.Create(Composition.Models.HostLocalRegistration.Create(
                Composition.MetadataCategoryIds.PersonalData,
                MetadataSchemaVersions.PersonalData,
                PrivacyRegistrationTemplate.FqnFor(RegistrationNamespace(rootNs))));
        });
    }

    /// <summary>Where the per-assembly privacy types are emitted.</summary>
    private static string RegistrationNamespace(string rootNamespace)
        => GeneratedRegistrationNames.InGeneratedNamespace(rootNamespace);

    /// <summary>
    ///     Works out which entities can have a subject-driven adapter, reporting the ones that cannot.
    /// </summary>
    /// <remarks>
    ///     Silent on everything except an identifier type nothing can look up by (PRAG2908). An entity
    ///     with no route is already PRAG2900's subject, and repeating it here would say the same thing
    ///     twice about one declaration.
    /// </remarks>
    private static List<(PrivacyEntityModel Entity, SubjectRoute Route, SubjectIdentifierMatch Match)> PlanAdapters(
        DetectedFeatures detected,
        List<PrivacyEntityModel> entities,
        IReadOnlyList<PrivacyEntityModel> reachable)
    {
        var planned = new List<(PrivacyEntityModel, SubjectRoute, SubjectIdentifierMatch)>();

        // The adapters read a DbContext, so they belong where EF Core is, and they implement interfaces
        // that live in the runtime package rather than in the zero-dependency attributes one.
        if (!detected.HasPrivacyRuntime || !detected.HasPersistenceEFCore)
            return planned;

        foreach (var entity in reachable)
        {
            if (!entity.HasPersonalData)
                continue;

            var route = SubjectGraphBuilder.RouteTo(entity, entities);
            if (route is null)
                continue;

            var match = SubjectIdentifierMatch.For(route.IdentifierTypeDisplay);
            if (match is null)
                continue;

            planned.Add((entity, route, match));
        }

        return planned;
    }

    /// <summary>Emits the per-entity access and erasure adapters, and reports what it could not emit.</summary>
    private static List<PrivacyAdapterRegistration> GenerateAdapters(
        SourceProductionContext ctx,
        DetectedFeatures detected,
        List<PrivacyEntityModel> entities,
        IReadOnlyList<PrivacyEntityModel> reachable)
    {
        var registrations = new List<PrivacyAdapterRegistration>();

        if (!detected.HasPrivacyRuntime || !detected.HasPersistenceEFCore)
            return registrations;

        ReportUnsupportedIdentifiers(ctx, reachable);

        foreach (var (entity, route, match) in PlanAdapters(detected, entities, reachable))
        {
            var source = new PersonalDataSourceTemplate(entity, route, match);
            var step = new ErasureStepTemplate(entity, route, match);

            SourceOutput.AddSource(ctx, source.RenderOutput());
            SourceOutput.AddSource(ctx, step.RenderOutput());

            registrations.Add(new PrivacyAdapterRegistration
            {
                EntityFullTypeName = entity.FullTypeName,
                SourceFqn = Qualify(entity, NamingHelper.AppendSuffix(entity.TypeName, "PersonalDataSource")),
                ErasureStepFqn = Qualify(entity, NamingHelper.AppendSuffix(entity.TypeName, "ErasureStep"))
            });
        }

        return registrations;
    }

    /// <summary>
    ///     PRAG2908 — once per subject, against the subject rather than against every entity that
    ///     reaches it.
    /// </summary>
    private static void ReportUnsupportedIdentifiers(
        SourceProductionContext ctx, IReadOnlyList<PrivacyEntityModel> reachable)
    {
        foreach (var subject in reachable)
        {
            if (!subject.IsSubject)
                continue;

            var identifier = subject.Properties.FirstOrDefault(p => p.Name == subject.SubjectIdentifier);
            if (identifier is null || SubjectIdentifierMatch.For(identifier.TypeDisplay) is not null)
                continue;

            Report(ctx, PrivacyDiagnostics.UnsupportedSubjectIdentifierType, identifier.Location,
                subject.TypeName, identifier.Name, identifier.TypeDisplay);
        }
    }

    /// <summary>Emits the assembly's processing-activity source, and returns the type to register.</summary>
    private static string? GenerateActivitySource(
        SourceProductionContext ctx,
        DetectedFeatures detected,
        IReadOnlyList<PrivacyEntityModel> reachable,
        IReadOnlyList<EndpointModel> endpoints,
        string rootNamespace)
    {
        var fqn = ActivitySourceFqn(detected, reachable, rootNamespace);
        if (fqn is null)
            return null;

        SourceOutput.AddSource(
            ctx,
            new ProcessingActivitySourceTemplate(
                reachable, endpoints, RegistrationNamespace(rootNamespace)).RenderOutput());

        return fqn;
    }

    private static string? ActivitySourceFqn(
        DetectedFeatures detected, IReadOnlyList<PrivacyEntityModel> reachable, string rootNamespace)
        // No database needed: these are declarations about the code. A module with classified data and
        // no persistence still has to appear in the register.
        => detected.HasPrivacyRuntime && reachable.Any(e => e.HasPersonalData)
            ? ProcessingActivitySourceTemplate.FqnFor(RegistrationNamespace(rootNamespace))
            : null;

    /// <summary>Emits the one call that puts the generated adapters into DI.</summary>
    /// <returns>The <c>Namespace.Class.Method</c> to call, or null when nothing was generated.</returns>
    private static string? GenerateRegistration(
        SourceProductionContext ctx,
        List<PrivacyAdapterRegistration> adapters,
        string? activitySourceFqn,
        string rootNamespace)
    {
        if (adapters.Count == 0 && activitySourceFqn is null)
            return null;

        SourceOutput.AddSource(
            ctx,
            new PrivacyRegistrationTemplate(
                adapters, activitySourceFqn, RegistrationNamespace(rootNamespace)).RenderOutput());

        return PrivacyRegistrationTemplate.FqnFor(RegistrationNamespace(rootNamespace));
    }

    private static string Qualify(PrivacyEntityModel entity, string typeName)
        => string.IsNullOrEmpty(entity.Namespace) ? typeName : $"{entity.Namespace}.{typeName}";

    private static IncrementalValuesProvider<PrivacyEntityModel> TypesWith(
        IncrementalGeneratorInitializationContext context, string attributeFqn)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax,
                static (ctx, _) => PrivacyEntityTransform.FromType(ctx.TargetSymbol as INamedTypeSymbol))
            .Where(static m => m is not null)!;

    /// <summary>
    ///     Types reached through a classified <em>property</em>.
    /// </summary>
    /// <remarks>
    ///     Needed on its own: an entity that classifies personal data but declares no path to a subject
    ///     is precisely what PRAG2900 reports, and neither class-level trigger would ever see it.
    /// </remarks>
    private static IncrementalValuesProvider<PrivacyEntityModel> TypesWithClassifiedProperties(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.PrivacyPersonalData,
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax,
                static (ctx, _) => PrivacyEntityTransform.FromType(ctx.TargetSymbol.ContainingType))
            .Where(static m => m is not null)!;

    /// <summary>
    ///     Merges the three triggers, keeping one model per type.
    /// </summary>
    /// <remarks>
    ///     A type carrying two of the attributes arrives twice, and every classified property on it
    ///     arrives once more. The models are identical — the transform reads the whole type each time —
    ///     so the first one wins and the duplicates are dropped.
    /// </remarks>
    private static List<PrivacyEntityModel> Deduplicate(params IEnumerable<PrivacyEntityModel>[] sources)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PrivacyEntityModel>();

        foreach (var source in sources)
        foreach (var model in source)
            if (seen.Add(model.FullTypeName))
                result.Add(model);

        return WithoutWhatAnotherEntityFoldsIn(result);
    }

    /// <summary>
    ///     Drops the types that are only reached <em>through</em> another entity: what it owns, and what
    ///     it inherits.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An owned record classifying a member arrives here as an entity in its own right — the
    ///         <c>[PersonalData]</c> trigger sees the property and hands back its containing type — and
    ///         it declares no path to a subject, because it does not need one: its owner has it. Left
    ///         in, it would be reported as data that can never be erased (PRAG2900) while its owner's
    ///         plan is erasing it, and every unclassified string on it would be reported twice on the
    ///         same line. Analysed through its owner, the path says which record a column belongs to.
    ///     </para>
    ///     <para>
    ///         ⚠️ A <b>base class</b> arrives the same way and for the same reason, and has to go for
    ///         the same one: it has no row of its own, and the entity that inherits it is where its
    ///         columns live.
    ///     </para>
    /// </remarks>
    private static List<PrivacyEntityModel> WithoutWhatAnotherEntityFoldsIn(List<PrivacyEntityModel> entities)
    {
        var folded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities)
        foreach (var name in entity.FoldedTypeFullNames)
            folded.Add(name);

        if (folded.Count == 0)
            return entities;

        var kept = new List<PrivacyEntityModel>(entities.Count);
        foreach (var entity in entities)
            if (!folded.Contains(entity.FullTypeName))
                kept.Add(entity);

        return kept;
    }

    private static void ReportDiagnostics(
        SourceProductionContext ctx,
        List<PrivacyEntityModel> all,
        IReadOnlyList<PrivacyEntityModel> reachable)
    {
        var reachableNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in reachable)
            reachableNames.Add(e.FullTypeName);

        // ⚠️ Whether this compilation is an application at all. Reachability is decided where the
        // subject is, and a package is never its own: the [DataSubject] that owns a framework entity
        // lives in whoever uses it. Without this, PRAG2900 would make a library's classification an
        // error — so a package such as Pragmatic.Identity.Local could not classify the sign-in address
        // on LocalIdentity without breaking its own build.
        //
        // This is the class remark above ("nothing is reported until the compilation declares a
        // [DataSubject]") applied to PRAG2900.
        //
        // What it gives up, deliberately: an application that classifies personal data and declares no
        // subject at all is not refused. Such an application gets no erasure plan, no register
        // and no extractors either — the whole feature is inert, which is the louder signal.
        var isAnApplication = false;
        foreach (var entity in all)
        {
            if (!entity.IsSubject)
                continue;

            isAnApplication = true;
            break;
        }

        foreach (var entity in all)
        {
            // A declared path that cannot be followed is reported even when the entity is unreachable —
            // the broken path is usually the reason it is unreachable.
            if (entity.HasUnresolvablePath)
                Report(ctx, PrivacyDiagnostics.InvalidSubjectPath, entity.Location,
                    entity.FullTypeName, entity.SubjectPath!);

            if (isAnApplication && entity.HasPersonalData && !reachableNames.Contains(entity.FullTypeName))
                Report(ctx, PrivacyDiagnostics.NoPathToSubject, entity.Location, entity.FullTypeName);

            foreach (var property in entity.Properties)
            {
                if (property.Classification is { } c)
                {
                    if (c.IsRetainedWithoutReason)
                        Report(ctx, PrivacyDiagnostics.RetainWithoutReason, property.Location,
                            entity.TypeName, property.Name);

                    if (c.IsKeyDestructionWithoutEncryption)
                        Report(ctx, PrivacyDiagnostics.DestroyKeyWithoutEncryption, property.Location,
                            entity.TypeName, property.Name);

                    // Same gate as UnwritableProperty below: only where a plan is generated. Reported at
                    // compile time because the alternative is finding out at SaveChanges, while serving
                    // an erasure request.
                    if (reachableNames.Contains(entity.FullTypeName) &&
                        c.NullsTheField && property.IsNonNullable)
                        Report(ctx, PrivacyDiagnostics.NullOnNonNullableProperty, property.Location,
                            entity.TypeName, property.Name, property.TypeDisplay);

                    if (reachableNames.Contains(entity.FullTypeName) &&
                        c.Erasure == "Anonymize" && property.AnonymousValue is null)
                        Report(ctx, PrivacyDiagnostics.NoAnonymousValue, property.Location,
                            entity.TypeName, property.Name, property.TypeDisplay);

                    // Only where a plan is actually generated: on an unreachable entity PRAG2900 already
                    // says the data can never be erased, and a second diagnostic about how would be noise
                    // on top of the reason.
                    // The generated Set{Property} route exists on the entity, so it does not reach a
                    // property of something the entity owns: there, public is the only way in.
                    if (reachableNames.Contains(entity.FullTypeName) &&
                        c.WritesTheField && !property.IsPubliclySettable &&
                        (property.IsNested || !entity.IsPersistenceEntity))
                        Report(ctx, PrivacyDiagnostics.UnwritableProperty, property.Location,
                            entity.TypeName, property.Name, c.Erasure);
                }
                else if (reachableNames.Contains(entity.FullTypeName) && property.NeedsAClassificationDecision)
                {
                    Report(ctx, PrivacyDiagnostics.UnclassifiedProperty, property.Location,
                        entity.TypeName, property.Name);
                }
            }
        }
    }

    /// <summary>
    ///     PRAG2904 — special-category data behind an endpoint that does not say who may read it.
    /// </summary>
    /// <remarks>
    ///     Runs over <em>all</em> entities rather than only the reachable ones. Reachability decides
    ///     whether something can be <em>erased</em>; it has no bearing on whether it is over-exposed, and
    ///     an entity that is unreachable — the thing PRAG2900 already complains about — is if anything
    ///     the more worrying place to find health data behind an open endpoint.
    /// </remarks>
    private static void ReportExposures(
        SourceProductionContext ctx,
        List<PrivacyEntityModel> entities,
        ImmutableArray<EndpointModel> endpoints)
    {
        foreach (var finding in SpecialCategoryExposure.Find(entities, endpoints))
            Report(ctx, PrivacyDiagnostics.SpecialCategoryWithoutPolicy, finding.Location,
                finding.Entity.TypeName, finding.Property.Name, finding.Endpoint.TypeName);

        // Asked to record its reads, with nothing to record into. Reported here rather than left to the
        // register alone: the register says the operation is unrecorded, which reads as a choice, and
        // this says the choice was made and could not be honoured.
        foreach (var endpoint in endpoints)
            if (endpoint is { IsValid: true, DeclaresRecordAccess: true, CanRecordAccess: false })
                Report(ctx, PrivacyDiagnostics.RecordAccessWithoutAuditTrail,
                    endpoint.LocationInfo, endpoint.TypeName);

        // Composes another operation and says nothing about what it reaches, so the register does not
        // list it — which reads as "processes nothing" rather than as "nobody answered".
        //
        // ⚠️ Reported against the FIRST unresolved dependency and not all of them. One message per
        // operation is what the author acts on; naming every one would turn a single omission into a
        // wall of warnings on the same line.
        foreach (var endpoint in endpoints)
            if (endpoint is { IsValid: true, IsDomainAction: true, DeclaresProcessedData: false }
                && !endpoint.UnresolvedDependencyTypes.IsDefaultOrEmpty)
                Report(ctx, PrivacyDiagnostics.CompositionWithoutDeclaredData,
                    endpoint.LocationInfo, endpoint.TypeName, endpoint.UnresolvedDependencyTypes[0]);

        // A declaration that restates an inference: two sources for one fact, which drift when the load
        // goes and the declaration stays.
        foreach (var endpoint in endpoints.Where(e => e.IsValid))
        foreach (var redundant in endpoint.RedundantDeclarations)
            Report(ctx, PrivacyDiagnostics.RedundantProcessesData,
                endpoint.LocationInfo, endpoint.TypeName, SimpleName(redundant));
    }

    private static string SimpleName(string fullyQualified)
        => fullyQualified.Substring(fullyQualified.LastIndexOf('.') + 1);

    private static void Report(
        SourceProductionContext ctx, DiagnosticDescriptor descriptor, LocationInfo? at, params object[] args)
        => ctx.ReportDiagnostic(Diagnostic.Create(descriptor, at?.ToLocation(), args));
}
