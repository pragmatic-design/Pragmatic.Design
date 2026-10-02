using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Additional diagnostic-emission tests covering IDs not exercised by <see cref="DiagnosticTests" />.
///     Verified against the emission logic in MappingFeature.GenerateSource.
/// </summary>
public class DiagnosticEdgeCaseTests : MappingGeneratorTestBase
{
    // =========================================================================
    // PRAG0307: Required property not mapped (Warning)
    // =========================================================================

    [Fact]
    public void RequiredProperty_WithoutMatchingSource_EmitsPrag0307()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }

                             // Required but no matching source property
                             public required string Name { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // PRAG0307 is emitted for required targets with no matching source property.
        HasDiagnostic(result, "PRAG0307").Should().BeTrue(
            "a required target property with no matching source should emit PRAG0307");
    }

    [Fact]
    public void RequiredProperty_WithMatchingSource_DoesNotEmitPrag0307()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }
                             public required string Name { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0307").Should().BeFalse(
            "a required property with a matching source must not emit PRAG0307");
    }

    // =========================================================================
    // PRAG0324: ID property excluded from ToEntity (Info)
    // =========================================================================

    [Fact]
    public void MapTo_WithIdProperty_EmitsPrag0324()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapTo<Entity>]
                         public partial record CreateDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // ID is excluded from ToEntity by default; the generator reports PRAG0324 (info).
        HasDiagnostic(result, "PRAG0324").Should().BeTrue(
            "an Id property on a [MapTo] DTO should emit PRAG0324 (excluded from ToEntity)");
    }
}
