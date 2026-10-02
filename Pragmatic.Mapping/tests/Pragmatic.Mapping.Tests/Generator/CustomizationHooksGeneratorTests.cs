using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for the generated customization hooks (<c>BeforeMapping</c> / <c>CustomizeMapping</c>)
///     and the <c>{Type}MappingContext</c> struct emitted for every <c>[MapFrom]</c> type.
///     Verified against MappingTemplate.RenderPartialMethods / RenderContextStruct.
/// </summary>
public class CustomizationHooksGeneratorTests : MappingGeneratorTestBase
{
    private const string Source = """
                                  using Pragmatic.Mapping.Attributes;

                                  namespace TestApp
                                  {
                                      public class User
                                      {
                                          public int Id { get; set; }
                                          public string Name { get; set; } = "";
                                      }

                                      [MapFrom<User>]
                                      public partial record UserDto
                                      {
                                          public int Id { get; init; }
                                          public string Name { get; init; } = "";
                                      }
                                  }
                                  """;

    [Fact]
    public void MapFrom_Always_GeneratesBeforeMappingPartialDeclaration()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse();

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().NotBeNull();
        // Partial method declaration (user supplies the body in their own partial).
        mainSource.Should().Contain("static partial void BeforeMapping(");
        mainSource.Should().Contain("ref UserDto? result");
    }

    [Fact]
    public void MapFrom_Always_GeneratesCustomizeMappingPartialDeclaration()
    {
        var result = RunGenerator(Source);

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().NotBeNull();
        mainSource.Should().Contain("static partial void CustomizeMapping(");
        mainSource.Should().Contain("ref UserDtoMappingContext ctx");
    }

    [Fact]
    public void MapFrom_FromEntity_InvokesBothHooks()
    {
        var result = RunGenerator(Source);

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().NotBeNull();
        // FromEntity calls the hooks at the documented points.
        mainSource.Should().Contain("BeforeMapping(entity, ref customResult);");
        mainSource.Should().Contain("CustomizeMapping(entity, ref ctx);");
    }

    [Fact]
    public void MapFrom_Always_GeneratesMappingContextStruct()
    {
        var result = RunGenerator(Source);

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().NotBeNull();
        // The mutable context struct carries one mutable field per mapped property.
        mainSource.Should().Contain("struct UserDtoMappingContext");
        mainSource.Should().Contain("public int Id;");
        mainSource.Should().Contain("public string Name;");
    }

    [Fact]
    public void CustomizeMapping_WithGenerateProjection_EmitsPrag0319()
    {
        // The CustomizeMapping hook is ignored in projection; the generator warns via PRAG0319
        // when a user-supplied CustomizeMapping partial exists alongside [GenerateProjection].
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class User
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapFrom<User>]
                         [GenerateProjection]
                         public partial record UserDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";

                             static partial void CustomizeMapping(User source, ref UserDtoMappingContext ctx);
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0319").Should().BeTrue(
            "CustomizeMapping defined alongside [GenerateProjection] should emit PRAG0319");
    }
}
