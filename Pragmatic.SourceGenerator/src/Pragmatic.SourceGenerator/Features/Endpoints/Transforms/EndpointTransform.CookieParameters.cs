using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Cookie parameter parsing ([FromCookie]).
/// </summary>
internal static partial class EndpointTransform
{
    private static ImmutableArray<CookieParameterModel> ParseCookieParameters(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p =>
            {
                var cookieAttr = p.GetAttributes()
                    .FirstOrDefault(a => IsFromCookieAttribute(a));

                if (cookieAttr is null)
                    return null;

                // Cookie name from constructor argument, or the property name
                var cookieName = cookieAttr.ConstructorArguments.Length > 0
                    ? cookieAttr.ConstructorArguments[0].Value?.ToString() ?? p.Name
                    : p.Name;

                var isRequired = true;
                foreach (var namedArg in cookieAttr.NamedArguments)
                    if (namedArg.Key == "IsRequired")
                        isRequired = (bool)(namedArg.Value.Value ?? true);

                var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var needsConversion = typeName != "string" && typeName != "string?";

                return new CookieParameterModel
                {
                    CookieName = cookieName,
                    PropertyName = p.Name,
                    TypeName = typeName,
                    IsRequired = isRequired,
                    NeedsConversion = needsConversion,
                    IsInitOnly = p.SetMethod?.IsInitOnly == true,
                    InitOnlyFallback = GetInitOnlyFallback(p)
                };
            })
            .Where(c => c is not null)
            .Select(c => c!)
            .ToImmutableArray();
    }
}
