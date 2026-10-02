using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.ValueObject.Diagnostics;
using Pragmatic.SourceGenerator.Features.ValueObject.Templates;
using Pragmatic.SourceGenerator.Features.ValueObject.Transforms;

namespace Pragmatic.SourceGenerator.Features.ValueObject;

/// <summary>
///     Standalone feature: generates Create/CreateUnsafe factory methods for records
///     marked with [ValueObject].
/// </summary>
internal static class ValueObjectFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeNames.ValueObject,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => ValueObjectTransform.Transform(ctx, ct))
            .Where(static m => m is not null);

        context.RegisterSourceOutputSafe(models, static (spc, model) =>
        {
            if (model is null) return;

            if (!model.IsPartial)
                spc.ReportDiagnostic(Diagnostic.Create(
                    ValueObjectDiagnostics.NotPartial, Location.None, model.TypeName));

            if (!model.HasValidate)
                spc.ReportDiagnostic(Diagnostic.Create(
                    ValueObjectDiagnostics.MissingValidate, Location.None, model.TypeName));

            var template = new ValueObjectTemplate(model);
            var artifact = template.RenderOutput();
            if (!artifact.IsEmpty)
                spc.AddSource(artifact);
        });
    }
}
