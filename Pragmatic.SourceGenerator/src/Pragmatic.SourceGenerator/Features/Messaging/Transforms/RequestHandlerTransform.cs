using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     Extracts <see cref="RequestHandlerModel"/> from [RequestHandler]-decorated classes
///     that implement <c>IRequestHandler&lt;TRequest, TResponse&gt;</c>.
/// </summary>
internal static class RequestHandlerTransform
{
    public static RequestHandlerModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Find IRequestHandler<TRequest, TResponse> implementation
        string? requestTypeFqn = null;
        string? responseTypeFqn = null;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface is { IsGenericType: true, Name: "IRequestHandler", TypeArguments.Length: 2 })
            {
                requestTypeFqn = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                responseTypeFqn = iface.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                break;
            }
        }

        // Must implement IRequestHandler<TRequest, TResponse>
        if (requestTypeFqn is null || responseTypeFqn is null)
            return null;

        return new RequestHandlerModel
        {
            Namespace = symbol.GetNamespaceOrEmpty(),
            TypeName = symbol.Name,
            Accessibility = symbol.GetAccessibilityKeyword(),
            TypeKind = symbol.GetTypeKindKeyword(),
            RequestTypeFqn = requestTypeFqn,
            ResponseTypeFqn = responseTypeFqn,
            LocationInfo = Pragmatic.SourceGenerator.Core.LocationInfo.From(symbol.Locations.Length > 0 ? symbol.Locations[0] : null),
        };
    }
}
