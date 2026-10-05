using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Redaction.Transforms;

/// <summary>
///     The JSON shape of a type the redaction map carries, as a root of the generated JSON context.
/// </summary>
/// <remarks>
///     <para>
///         The redactor serializes a redacted value before it masks it. Through reflection that throws
///         under Native AOT, inside the logger, and the entry is lost. With the type in the generated
///         context the redactor serializes it from metadata this generator wrote, the same way a
///         <c>[MapFrom]</c> DTO is.
///     </para>
///     <para>
///         The question "is this type redacted" is <see cref="DeclaredRedactionPathTransform.IsRedacted" />,
///         the one the map is built from, so the two cannot disagree about the set.
///     </para>
/// </remarks>
internal static class RedactedTypeJsonTransform
{
    public static JsonRootContribution? Transform(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, ct) is not INamedTypeSymbol type)
            return null;

        // An open generic is never the runtime type of a logged value; its closed forms are not
        // declared anywhere this transform can see.
        if (type.TypeParameters.Length > 0)
            return null;

        if (!DeclaredRedactionPathTransform.IsRedacted(type, ct))
            return null;

        return JsonShapeExtractor.TryExtractClosure(type, out var objects, out var leaves, out var collections)
            ? new JsonRootContribution(objects, leaves, collections)
            : null;
    }
}
