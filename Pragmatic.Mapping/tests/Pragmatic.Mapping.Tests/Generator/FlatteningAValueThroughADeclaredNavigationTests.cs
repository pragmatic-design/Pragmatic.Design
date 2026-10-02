using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Flattening by convention to a value-type member through a navigation declared by
///     hand compiles.
/// </summary>
/// <remarks>
///     <c>CustomerRating</c> is <c>entity.Customer?.Rating</c>: the <c>?.</c> makes an <c>int</c> an
///     <c>int?</c>. The declared-navigation branch took its nullability from the annotations — both
///     non-nullable — so the templates added neither the default nor the fold that turn it back into an
///     <c>int</c>, and neither mapping compiled. Found on a <c>decimal</c>.
/// </remarks>
public class FlatteningAValueThroughADeclaredNavigationTests : MappingGeneratorTestBase
{
    private static string Source(string dtoMember) => $$"""
        using Pragmatic.Mapping.Attributes;

        namespace Contoso
        {
            public class Customer
            {
                public int Rating { get; set; }
                public string Name { get; set; } = "";
            }

            public class Invoice
            {
                public Customer Customer { get; set; } = new();
            }

            [MapFrom<Invoice>]
            [GenerateProjection]
            public partial class InvoiceDto
            {
                {{dtoMember}}
            }
        }
        """;

    [Fact]
    public void AValueTypeMember_Compiles_InBothMappings()
    {
        var result = RunGenerator(Source("public int CustomerRating { get; init; }"));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var mapping = GetGeneratedSource(result, "InvoiceDto.Mapping");
        mapping.Should().Contain("CustomerRating = (entity.Customer?.Rating).GetValueOrDefault(),",
            "in memory a null navigation gives the member type's default, not a null an int cannot hold");
        mapping.Should().Contain("CustomerRating = (entity.Customer == null ? 0 : entity.Customer.Rating),",
            "the projection folds the same default into its null branch");
    }

    /// <summary>The control: a reference-type member is generated as it always was.</summary>
    [Fact]
    public void AStringMember_IsUnchanged()
    {
        var result = RunGenerator(Source("""public string CustomerName { get; init; } = "";"""));

        HasCompilationErrors(result).Should().BeFalse();
        GetGeneratedSource(result, "InvoiceDto.Mapping").Should().Contain("CustomerName = entity.Customer?.Name,");
    }
}
