using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Reads <see cref="ConsumerPlatform" /> from a compilation: the language version from its options, each
///     capability from the presence of the API the emitted line would call.
/// </summary>
internal static class ConsumerPlatformDetector
{
    public static ConsumerPlatform Detect(Compilation compilation) => new()
    {
        LanguageVersion = compilation is CSharpCompilation csharp ? (int)csharp.LanguageVersion : 0,
        HasJsonTypeInfoOfT = HasGenericParameterlessMethod(compilation, "System.Text.Json.JsonSerializerOptions", "GetTypeInfo"),
    };

    /// <summary>A public instance method with one type parameter and no parameters, on the named type.</summary>
    private static bool HasGenericParameterlessMethod(Compilation compilation, string typeName, string methodName)
        => compilation.GetTypeByMetadataName(typeName) is { } type
           && type.GetMembers(methodName).OfType<IMethodSymbol>().Any(static m =>
               m is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, TypeParameters.Length: 1, Parameters.Length: 0 });
}
