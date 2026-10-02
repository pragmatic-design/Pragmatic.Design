using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Manifest.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     <c>ErrorExtensionExtractor</c> decides which properties of an error type become ProblemDetails
///     extensions in the generated OpenAPI and typed fields in generated clients. A miss here is
///     invisible at build time and only shows up as a missing field in a consumer.
/// </summary>
public class ErrorExtensionExtractorTests
{
    private const string ErrorBase = """
        namespace Pragmatic
        {
            public abstract class Error
            {
                public string? Code { get; init; }
                public int StatusCode { get; init; }
                public string Title { get; init; } = "";
                public string? Description { get; init; }
                public bool IsTransient { get; init; }
            }
        }
        """;

    private static INamedTypeSymbol Symbol(string source, string metadataName)
    {
        var compilation = ManifestTestHarness.Compile(source);
        return compilation.GetTypeByMetadataName(metadataName)!;
    }

    [Fact]
    public void Extract_CustomProperties_AreCamelCasedWithTheirDeclaredType()
    {
        var symbol = Symbol(ErrorBase + """

            namespace App
            {
                public sealed class RoomNotAvailableError : Pragmatic.Error
                {
                    public System.Guid RoomId { get; init; }
                    public int NightsRequested { get; init; }
                }
            }
            """, "App.RoomNotAvailableError");

        var extensions = ErrorExtensionExtractor.ExtractFromSymbol(symbol);

        extensions.Select(e => e.Name).Should().BeEquivalentTo("roomId", "nightsRequested");
        extensions.Single(e => e.Name == "roomId").Type.Should().EndWith("Guid");
        extensions.Single(e => e.Name == "nightsRequested").Type.Should().Be("int");
    }

    [Fact]
    public void Extract_StopsAtThePragmaticErrorBase_SoBaseFieldsAreNotExtensions()
    {
        var symbol = Symbol(ErrorBase + """

            namespace App
            {
                public abstract class DomainError : Pragmatic.Error { public string Boundary { get; init; } = ""; }
                public sealed class ConflictError : DomainError { public System.Guid EntityId { get; init; } }
            }
            """, "App.ConflictError");

        var extensions = ErrorExtensionExtractor.ExtractFromSymbol(symbol);

        // The intermediate class contributes; the Pragmatic.Error base and its members do not.
        extensions.Select(e => e.Name).Should().BeEquivalentTo("entityId", "boundary");
        extensions.Select(e => e.Name).Should().NotContain("code");
        extensions.Select(e => e.Name).Should().NotContain("statusCode");
    }

    [Fact]
    public void Extract_BasePropertyNamesAreFilteredEvenWhenRedeclared()
    {
        // A record error redeclares Code/Title as positional members and gets EqualityContract for
        // free; none of those are ProblemDetails extensions.
        var symbol = Symbol("""
            namespace App
            {
                public sealed record NotFoundError(string Code, string Title, string ResourceName);
            }
            """, "App.NotFoundError");

        var extensions = ErrorExtensionExtractor.ExtractFromSymbol(symbol);

        extensions.Select(e => e.Name).Should().Equal("resourceName");
    }

    [Fact]
    public void Extract_NonPublicAndWriteOnlyPropertiesAreIgnored()
    {
        var symbol = Symbol("""
            namespace App
            {
                public sealed class WeirdError
                {
                    internal int Hidden { get; init; }
                    private string Secret { get; init; } = "";
                    public string Visible { get; init; } = "";
                    public string WriteOnly { set { } }
                }
            }
            """, "App.WeirdError");

        var extensions = ErrorExtensionExtractor.ExtractFromSymbol(symbol);

        extensions.Select(e => e.Name).Should().Equal("visible");
    }

    [Fact]
    public void Extract_UnresolvableTypeNameOrNoCompilation_YieldsNoExtensions()
    {
        var compilation = ManifestTestHarness.Compile("namespace App { public class Nothing { } }");

        ErrorExtensionExtractor.Extract("global::App.DoesNotExist", compilation).Should().BeEmpty();
        ErrorExtensionExtractor.Extract("global::App.Nothing", compilation: null).Should().BeEmpty();
    }

    [Fact]
    public void Extract_StripsTheGlobalPrefixBeforeResolving()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace App { public sealed class TimeoutError { public int AfterSeconds { get; init; } } }
            """);

        ErrorExtensionExtractor.Extract("global::App.TimeoutError", compilation)
            .Select(e => e.Name).Should().Equal("afterSeconds");
    }
}
