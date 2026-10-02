using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Pragmatic.SourceGen.Tests;

/// <summary>
///     Tests for shared SymbolExtensions used in source generators.
///     These helpers are critical - bugs here cascade to all generated code.
/// </summary>
public class SymbolExtensionsTests
{
    // =========================================================================
    // ToRenderName Tests - Primitives
    // =========================================================================

    [Theory]
    [InlineData("int", "int")]
    [InlineData("string", "string")]
    [InlineData("bool", "bool")]
    [InlineData("long", "long")]
    [InlineData("double", "double")]
    [InlineData("decimal", "decimal")]
    [InlineData("float", "float")]
    [InlineData("char", "char")]
    [InlineData("byte", "byte")]
    [InlineData("short", "short")]
    [InlineData("object", "object")]
    public void ToRenderName_PrimitiveTypes_ReturnsCSharpKeyword(string typeName, string expected)
    {
        // Arrange
        var typeSymbol = GetTypeSymbol(typeName);

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("int?", "int?")]
    [InlineData("bool?", "bool?")]
    [InlineData("decimal?", "decimal?")]
    public void ToRenderName_NullablePrimitives_ReturnsCSharpKeywordWithQuestionMark(string typeName, string expected)
    {
        // Arrange
        var typeSymbol = GetTypeSymbol(typeName);

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be(expected);
    }

    // =========================================================================
    // ToRenderName Tests - Complex Types
    // =========================================================================

    [Fact]
    public void ToRenderName_SystemDateTime_ReturnsGlobalPrefixed()
    {
        // Arrange
        var typeSymbol = GetTypeSymbol("System.DateTime");

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::System.DateTime");
    }

    [Fact]
    public void ToRenderName_SystemGuid_ReturnsGlobalPrefixed()
    {
        // Arrange
        var typeSymbol = GetTypeSymbol("System.Guid");

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::System.Guid");
    }

    [Fact]
    public void ToRenderName_CustomClass_ReturnsGlobalPrefixed()
    {
        // Arrange
        var source = @"
namespace MyApp.Models
{
    public class User { }
}";
        var typeSymbol = GetTypeSymbolFromSource(source, "MyApp.Models.User");

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::MyApp.Models.User");
    }

    [Fact]
    public void ToRenderName_NullableCustomClass_ReturnsGlobalPrefixedWithQuestionMark()
    {
        // Arrange
        var source = @"
#nullable enable
namespace MyApp.Models
{
    public class User { }
    public class Container
    {
        public User? NullableUser { get; set; }
    }
}";
        var compilation = CreateCompilation(source);
        var containerType = compilation.GetTypeByMetadataName("MyApp.Models.Container")!;
        var userProperty = containerType.GetMembers("NullableUser").OfType<IPropertySymbol>().First();
        var typeSymbol = userProperty.Type;

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::MyApp.Models.User?");
    }

    // =========================================================================
    // ToRenderName Tests - Arrays
    // =========================================================================

    [Fact]
    public void ToRenderName_IntArray_ReturnsPrimitiveArray()
    {
        // Arrange
        var typeSymbol = GetTypeSymbol("int[]");

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("int[]");
    }

    [Fact]
    public void ToRenderName_StringArray_ReturnsPrimitiveArray()
    {
        // Arrange
        var typeSymbol = GetTypeSymbol("string[]");

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("string[]");
    }

    [Fact]
    public void ToRenderName_CustomTypeArray_ReturnsGlobalPrefixedArray()
    {
        // Arrange
        var source = @"
namespace MyApp.Models
{
    public class User { }
    public class Container
    {
        public User[] Users { get; set; }
    }
}";
        var compilation = CreateCompilation(source);
        var containerType = compilation.GetTypeByMetadataName("MyApp.Models.Container")!;
        var usersProperty = containerType.GetMembers("Users").OfType<IPropertySymbol>().First();
        var typeSymbol = usersProperty.Type;

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::MyApp.Models.User[]");
    }

    // =========================================================================
    // ToRenderName Tests - Generics
    // =========================================================================

    [Fact]
    public void ToRenderName_ListOfInt_ReturnsCorrectGeneric()
    {
        // Arrange
        var source = @"
using System.Collections.Generic;
public class Container
{
    public List<int> Numbers { get; set; }
}";
        var compilation = CreateCompilation(source);
        var containerType = compilation.GetTypeByMetadataName("Container")!;
        var numbersProperty = containerType.GetMembers("Numbers").OfType<IPropertySymbol>().First();
        var typeSymbol = numbersProperty.Type;

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::System.Collections.Generic.List<int>");
    }

    [Fact]
    public void ToRenderName_DictionaryOfStringToInt_ReturnsCorrectGeneric()
    {
        // Arrange
        var source = @"
using System.Collections.Generic;
public class Container
{
    public Dictionary<string, int> Map { get; set; }
}";
        var compilation = CreateCompilation(source);
        var containerType = compilation.GetTypeByMetadataName("Container")!;
        var mapProperty = containerType.GetMembers("Map").OfType<IPropertySymbol>().First();
        var typeSymbol = mapProperty.Type;

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::System.Collections.Generic.Dictionary<string, int>");
    }

    [Fact]
    public void ToRenderName_ListOfCustomType_ReturnsCorrectGeneric()
    {
        // Arrange
        var source = @"
using System.Collections.Generic;
namespace MyApp.Models
{
    public class User { }
    public class Container
    {
        public List<User> Users { get; set; }
    }
}";
        var compilation = CreateCompilation(source);
        var containerType = compilation.GetTypeByMetadataName("MyApp.Models.Container")!;
        var usersProperty = containerType.GetMembers("Users").OfType<IPropertySymbol>().First();
        var typeSymbol = usersProperty.Type;

        // Act
        var result = typeSymbol.ToRenderName();

        // Assert
        result.Should().Be("global::System.Collections.Generic.List<global::MyApp.Models.User>");
    }

    // =========================================================================
    // ToSimpleName Tests
    // =========================================================================

    [Theory]
    [InlineData("int", "int")]
    [InlineData("string", "string")]
    [InlineData("bool?", "bool?")]
    public void ToSimpleName_Primitives_ReturnsCSharpKeyword(string typeName, string expected)
    {
        // Arrange
        var typeSymbol = GetTypeSymbol(typeName);

        // Act
        var result = typeSymbol.ToSimpleName();

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ToSimpleName_CustomClass_ReturnsNameWithoutNamespace()
    {
        // Arrange
        var source = @"
namespace MyApp.Models
{
    public class User { }
}";
        var typeSymbol = GetTypeSymbolFromSource(source, "MyApp.Models.User");

        // Act
        var result = typeSymbol.ToSimpleName();

        // Assert
        result.Should().Be("User");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        };

        // Add runtime assemblies
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references = references.Concat(new[]
        {
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Collections.dll"))
        }).ToArray();

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ITypeSymbol GetTypeSymbol(string typeName)
    {
        var source = $@"
public class TestClass
{{
    public {typeName} TestProperty {{ get; set; }}
}}";
        var compilation = CreateCompilation(source);
        var testClass = compilation.GetTypeByMetadataName("TestClass")!;
        var property = testClass.GetMembers("TestProperty").OfType<IPropertySymbol>().First();
        return property.Type;
    }

    private static ITypeSymbol GetTypeSymbolFromSource(string source, string fullTypeName)
    {
        var compilation = CreateCompilation(source);
        return compilation.GetTypeByMetadataName(fullTypeName)!;
    }
}