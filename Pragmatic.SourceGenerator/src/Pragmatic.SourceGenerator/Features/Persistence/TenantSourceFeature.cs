using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence;

/// <summary>
///     The <c>ITenantStore</c> of a <c>[TenantSource]</c> entity: written as <c>{Entity}.TenantStore</c>, and declared to the host
///     (<c>_Metadata.TenantSource.g.cs</c>), which registers it, wrapped by <c>ObservedTenantStore</c>, in place of the empty in-memory one.
/// </summary>
/// <remarks>
///     <para>
///         The module declares and the host composes: the store is a fact of the module that owns the entity, and the registration is the
///         host's, which alone sees every module and so alone can tell two sources apart (<c>PRAG1617</c>).
///     </para>
///     <para>
///         Part of Persistence, as the local identity store is: the store reads through the entity's repository, whichever engine persists
///         it, by specifications in the entity's shape.
///     </para>
/// </remarks>
internal static class TenantSourceFeature
{
    private const string SchemaVersion = "1.0";

    /// <returns>The host-local entries, for a host that declares a tenant source itself.</returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<EntityMetadataModel>> entities,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        var problems = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.TenantSource,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) =>
                {
                    var type = (INamedTypeSymbol)ctx.TargetSymbol;
                    TenantSourceReader.Read(type, isAuditable: false, out var problem);
                    return problem is null ? null : new Problem(type.Name, problem, LocationInfo.From(type.Locations.FirstOrDefault()));
                })
            .Where(static p => p is not null);

        context.RegisterSourceOutputSafe(problems, static (ctx, problem) =>
            ctx.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.PersistenceDiagnostics.TenantSourceMemberInvalid,
                problem!.Location?.ToLocation() ?? Location.None,
                problem.Entity,
                problem.Reason)));

        var sources = entities.Combine(features)
            .Select(static (input, _) =>
            {
                var (all, detected) = input;
                return new EquatableArray<EntityMetadataModel>(SourcesOf(all, detected));
            });

        context.RegisterSourceOutputSafe(sources.Combine(features), static (ctx, input) =>
        {
            var (declared, detected) = input;
            foreach (var entity in declared)
            {
                ctx.AddSource(new TenantSourceStoreTemplate(entity).RenderOutput());
                if (detected.HasComposition)
                    ctx.AddSource(new TenantSourceMetadataTemplate(entity.TypeName, TenantSourceStoreTemplate.FactoryOf(entity)).RenderOutput());
            }
        });

        return sources.Combine(features).Select(static (input, _) =>
        {
            var (declared, detected) = input;
            if (!detected.HasComposition || declared.Count == 0)
                return EquatableArray<Composition.Models.MetadataEntry>.Empty;
            return new EquatableArray<Composition.Models.MetadataEntry>(declared
                .Select(entity => Composition.Models.HostLocalRegistration.CreatePayload(
                    Composition.MetadataCategoryIds.TenantSource,
                    SchemaVersion,
                    TenantSourceMetadataTemplate.Payload(entity.TypeName, TenantSourceStoreTemplate.FactoryOf(entity)),
                    TenantSourceStoreTemplate.FactoryOf(entity)))
                .ToImmutableArray());
        });
    }

    /// <summary>The entities that get a store: local, valid, declared, persisted by an engine.</summary>
    private static ImmutableArray<EntityMetadataModel> SourcesOf(ImmutableArray<EntityMetadataModel> entities, DetectedFeatures features)
    {
        if (!PersistenceEngineMarker.AnyReferenced(features) || entities.IsDefaultOrEmpty)
            return ImmutableArray<EntityMetadataModel>.Empty;

        return entities
            .Where(e => e is { IsValid: true, IsFromReference: false, TenantSource: not null } && PersistenceEngineMarker.HasEngine(e, features))
            .OrderBy(e => e.FullTypeName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private sealed record Problem(string Entity, string Reason, LocationInfo? Location);
}
