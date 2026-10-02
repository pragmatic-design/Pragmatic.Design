using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Compiles a snippet and hands back real Roslyn symbols, for unit tests that exercise a transform
///     helper directly rather than through a generator run.
/// </summary>
internal static class SymbolCompilationHelper
{
    public static Compilation Compile(string source)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ImmutableArray<>).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "netstandard.dll"))
        ];

        return CSharpCompilation.Create(
            "SymbolTestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "TestSource.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    public static INamedTypeSymbol GetType(string source, string fullyQualifiedMetadataName)
    {
        var symbol = Compile(source).GetTypeByMetadataName(fullyQualifiedMetadataName);
        symbol.Should().NotBeNull("type '{0}' should be present in the test compilation", fullyQualifiedMetadataName);
        return symbol!;
    }

    public static ITypeSymbol GetPropertyType(
        string source, string fullyQualifiedMetadataName, string propertyName)
    {
        var owner = GetType(source, fullyQualifiedMetadataName);
        var property = owner.GetMembers(propertyName).OfType<IPropertySymbol>().SingleOrDefault();
        property.Should().NotBeNull("property '{0}' should exist on '{1}'", propertyName, fullyQualifiedMetadataName);
        return property!.Type;
    }
}
