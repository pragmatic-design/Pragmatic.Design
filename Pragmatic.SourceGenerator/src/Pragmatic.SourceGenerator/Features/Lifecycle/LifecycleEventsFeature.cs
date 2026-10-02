using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Lifecycle.Diagnostics;
using Pragmatic.SourceGenerator.Features.Lifecycle.Templates;
using Pragmatic.SourceGenerator.Features.Lifecycle.Transforms;

namespace Pragmatic.SourceGenerator.Features.Lifecycle;

/// <summary>
///     Registers the <c>[Raises&lt;TEvent&gt;(on: ...)]</c> pipeline: for each entity, generate the
///     <c>IRaisesLifecycleEvents</c> partial that raises the declared events at their lifecycle transition.
///     Self-guarding — emits nothing unless an entity carries the attribute.
/// </summary>
internal static class LifecycleEventsFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Raises,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: LifecycleEventsTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutputSafe(models, static (ctx, model) =>
        {
            if (!model.IsDomainEventSource)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    LifecycleEventsDiagnostics.MustBeDomainEventSource,
                    model.Location ?? Location.None,
                    model.TypeName));
                return;
            }

            foreach (var unmatched in model.UnmatchedParameters.AsImmutableArray())
                ctx.ReportDiagnostic(Diagnostic.Create(
                    LifecycleEventsDiagnostics.UnmatchedConstructorParameter,
                    model.Location ?? Location.None,
                    unmatched,
                    model.TypeName));

            var artifact = new LifecycleEventsTemplate(model).RenderOutput();
            if (!artifact.IsEmpty)
                ctx.AddSource(artifact);
        });

        // The same attribute on a METHOD of an entity: nothing generates the raise, and the class-level
        // form on that same entity does — so the declaration reads as wired and is not.
        // A second pipeline, because the one above filters the syntax down to class declarations.
        var raisesOnMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.Raises,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: RaisesOnMethodTransform.Transform);

        context.RegisterSourceOutputSafe(raisesOnMethods, static (ctx, model) =>
        {
            if (!model.DeclaredOnAnEntity)
                return;

            foreach (var eventName in model.EventNames.AsImmutableArray())
                ctx.ReportDiagnostic(Diagnostic.Create(
                    LifecycleEventsDiagnostics.RaisesOnAnEntityMethodGeneratesNothing,
                    model.Location ?? Location.None,
                    model.Origin,
                    eventName));
        });
    }
}
