using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Actions.Templates;
using ShapeSourceModel = Pragmatic.SourceGenerator.Features.Persistence.Models.ShapeSourceModel;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     The invokers' sources as the engines bind them: the specifications an invoker writes (<c>[RequireExists]</c>, <c>[LoadEntities]</c>:
///     <c>Spec&lt;T&gt;.Where(…)</c>) are leaves a non-EF engine compiles where it sees them, and no generator sees another's output.
/// </summary>
/// <remarks>
///     The same models the invokers are emitted from, rendered by the same templates with the same inputs (a mutation's
///     <c>ValidationIsAvailable</c> as <see cref="GenerateMutationInvoker" /> gets it): an interceptor names its call site by the file's
///     content, so the text bound has to be the text emitted. As <c>EntityShapeArtifacts.ForBinding</c> does for the entities' shape.
/// </remarks>
internal static partial class ActionsFeature
{
    private static IncrementalValueProvider<EquatableArray<ShapeSourceModel>> InvokerSources(
        IncrementalValueProvider<ImmutableArray<ActionModel>> actions,
        IncrementalValueProvider<ImmutableArray<MutationModel>> mutations,
        IncrementalValueProvider<ImmutableArray<MutationModel>> programmaticMutations,
        IncrementalValueProvider<DetectedFeatures> features)
        => actions.Combine(mutations).Combine(programmaticMutations).Combine(features)
            .Select(static (pair, _) =>
            {
                var (((actionModels, mutationModels), programmatic), detected) = pair;
                var sources = new List<ShapeSourceModel>();
                foreach (var action in actionModels)
                    Add(sources, new InvokerTemplate(action).RenderOutput());
                foreach (var mutation in mutationModels)
                    Add(sources, new MutationInvokerTemplate(mutation with { ValidationIsAvailable = detected.HasValidation }).RenderOutput());
                foreach (var mutation in programmatic)
                    Add(sources, new MutationInvokerTemplate(mutation).RenderOutput());
                return sources.ToEquatableArray();
            });

    private static void Add(List<ShapeSourceModel> sources, Artifact artifact)
    {
        if (!artifact.IsEmpty)
            sources.Add(new ShapeSourceModel(artifact.HintName, artifact.Text));
    }
}
