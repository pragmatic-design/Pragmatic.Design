using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence;

internal static partial class EntityCoreFeature
{
    /// <summary>
    ///     <c>[ComputedDefault]</c>: one registration per assembly, the generators and the bindings every engine's unit of work applies at the
    ///     save (#740), driven by the properties as <c>[GeneratedValue]</c>'s is.
    /// </summary>
    private static void RegisterComputedDefaults(
        IncrementalGeneratorInitializationContext context, IncrementalValueProvider<DetectedFeatures> features)
    {
        var defaults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ComputedDefaultTransform.ComputedDefaultAttributeName,
                predicate: static (node, _) => node is PropertyDeclarationSyntax,
                transform: ComputedDefaultTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect()
            .Combine(features);

        context.RegisterSourceOutputSafe(defaults, static (ctx, pair) =>
        {
            if (pair.Left.Length == 0 || GeneratedValueEngineOf(pair.Right) is null)
                return;

            var artifact = new ComputedDefaultRegistrationTemplate(pair.Left.ToImmutableArray()).RenderOutput();
            if (!artifact.IsEmpty)
                ctx.AddSource(artifact);
        });
    }
}
