using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
/// Aggregate output from TraitFeature for downstream feature injection.
/// </summary>
internal sealed record TraitOutput
{
    /// <summary>Trait-generated entity infos for DbContext injection.</summary>
    public required IncrementalValueProvider<ImmutableArray<TraitEntityInfo>> TraitEntities { get; init; }

    /// <summary>Trait-generated ActionModels for ActionsFeature pipeline injection.</summary>
    public IncrementalValueProvider<ImmutableArray<ActionModel>>? Actions { get; init; }

    /// <summary>Trait-generated QueryModels for QueryFeature pipeline injection.</summary>
    public IncrementalValueProvider<ImmutableArray<QueryModel>>? Queries { get; init; }

    /// <summary>Trait-generated EndpointModels for EndpointsFeature pipeline injection.</summary>
    public IncrementalValueProvider<ImmutableArray<EndpointModel>>? Endpoints { get; init; }

    /// <summary>
    ///     Descriptions of the DTOs this feature generates, for the API manifest. The manifest cannot
    ///     discover them by resolving symbols — they do not exist in the compilation being analysed — so
    ///     they are contributed explicitly.
    /// </summary>
    public IncrementalValueProvider<ImmutableArray<Manifest.Models.ManifestTypeModel>>? ManifestTypes { get; init; }

    /// <summary>
    ///     Trait-generated JobModels for JobsFeature pipeline injection (the
    ///     <c>[HasAttachments(PurgeDeletedAfterDays = N)]</c> retention job).
    /// </summary>
    public IncrementalValueProvider<ImmutableArray<Jobs.Models.JobModel>>? Jobs { get; init; }
}
