using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Manifest.Models;

namespace Pragmatic.SourceGenerator.Features.Manifest.Transforms;

/// <summary>
///     Extracts custom (non-base) properties from error types at compile time.
///     These become ProblemDetails extensions in OpenAPI and error fields in the client.
/// </summary>
internal static class ErrorExtensionExtractor
{
    private static readonly HashSet<string> BasePropertyNames = new(StringComparer.Ordinal)
    {
        "Code", "StatusCode", "Title", "Description", "MessageKey", "Parameters",
        "IsTransient", "RetryAfter", "TitleKey", "DescriptionKey", "EqualityContract"
    };

    public static ImmutableArray<ManifestErrorExtensionModel> Extract(string errorTypeName, Compilation? compilation)
    {
        if (compilation is null)
            return ImmutableArray<ManifestErrorExtensionModel>.Empty;

        var fqn = errorTypeName.Replace("global::", "");
        var typeSymbol = compilation.GetTypeByMetadataName(fqn);
        if (typeSymbol is null)
            return ImmutableArray<ManifestErrorExtensionModel>.Empty;

        return ExtractFromSymbol(typeSymbol);
    }

    public static ImmutableArray<ManifestErrorExtensionModel> ExtractFromSymbol(INamedTypeSymbol typeSymbol)
    {
        var extensions = ImmutableArray.CreateBuilder<ManifestErrorExtensionModel>();
        var visited = new HashSet<string>();
        var current = typeSymbol;

        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            // Stop at the Error base class
            if (current.Name == "Error" && current.ContainingNamespace?.ToDisplayString() == "Pragmatic")
                break;

            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetMethod is null) continue;
                if (BasePropertyNames.Contains(prop.Name)) continue;
                if (!visited.Add(prop.Name)) continue;

                extensions.Add(new ManifestErrorExtensionModel
                {
                    Name = ToCamelCase(prop.Name),
                    Type = prop.Type.ToDisplayString()
                });
            }

            current = current.BaseType;
        }

        return extensions.ToImmutable();
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
