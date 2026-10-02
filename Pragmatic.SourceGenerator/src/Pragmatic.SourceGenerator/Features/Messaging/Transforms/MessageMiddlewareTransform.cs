using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     Reads a <c>[MessageMiddleware]</c> class into the model the registration is emitted from.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>It rejects nothing</b>, and that is deliberate. A class that does not implement
///         <c>IMessageMiddleware</c> is already reported by
///         <c>MessagingShapeDiagnosticTransform.MiddlewareShape</c> (PRAG0803), so deciding it a second
///         time here would either report one mistake twice or — the version this replaced — drop the
///         node in silence, which is the shape the silent-drop ratchet exists to count.
///     </para>
///     <para>
///         So the transform answers with facts and the feature composes them: it says what the class is
///         and whether it can serve, and <c>MessagingFeature</c> registers the ones that can.
///     </para>
/// </remarks>
internal static class MessageMiddlewareTransform
{
    public static MessageMiddlewareModel Transform(GeneratorAttributeSyntaxContext context, CancellationToken _)
    {
        var symbol = context.TargetSymbol as INamedTypeSymbol;

        var forMessageType = context.Attributes[0].NamedArguments
            .Where(a => a.Key == "ForMessageType")
            .Select(a => a.Value.Value as INamedTypeSymbol)
            .FirstOrDefault();

        return new MessageMiddlewareModel
        {
            TypeFqn = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
            // Abstract is not a mistake worth a diagnostic of its own — a base middleware is a normal
            // thing to write — it is simply not the type to register.
            CanServe = symbol is { IsAbstract: false }
                       && symbol.AllInterfaces.Any(i =>
                           i.Name == "IMessageMiddleware"
                           && i.ContainingNamespace?.ToDisplayString() == "Pragmatic.Messaging"),
            ForMessageTypeFqn = forMessageType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };
    }
}
