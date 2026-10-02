using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Flattening by convention reaches through a navigation the relation declares, as it
///     does through one written by hand.
/// </summary>
/// <remarks>
///     <c>CustomerName</c> is <c>Customer.Name</c>. The convention looked for <c>Customer</c> among the
///     properties declared on the type, and a navigation declared with <c>[Relation]</c> is generated,
///     so it is not one of them during this pass: <c>PRAG0303</c>, while the explicit
///     <c>[MapProperty("Customer.Name")]</c> resolved the same path.
/// </remarks>
public class FlatteningThroughADeclaredNavigationTests : MappingGeneratorTestBase
{
    private static string Source(string dtoMember) => $$"""
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace TestApp
        {
            [Entity]
            public partial class Customer
            {
                public string Name { get; set; } = "";
            }

            [Entity]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            public partial class Order
            {
                public string Reference { get; set; } = "";
            }

            [MapFrom<Order>]
            [GenerateProjection]
            public partial class OrderDto
            {
                public string Reference { get; init; } = "";
                {{dtoMember}}
            }
        }
        """;

    [Fact]
    public void TheConvention_FlattensThroughIt()
    {
        var result = RunGenerator(Source("""public string CustomerName { get; init; } = "";"""));

        GetDiagnosticsById(result, "PRAG0303").Should().BeEmpty();
        GetGeneratedSource(result, "OrderDto.Mapping").Should().Contain("entity.Customer?.Name");
    }

    /// <summary>
    ///     <c>CustomerId</c> is the foreign key the relation generates, read as its column — not a join to
    ///     read <c>Customer.Id</c>. The Showcase's DTOs went red on exactly this when the convention first
    ///     learned the navigation.
    /// </summary>
    [Fact]
    public void AGeneratedMemberWithTheVeryName_StillWins()
    {
        var result = RunGenerator(Source("public System.Guid CustomerId { get; init; }"));

        var mapping = GetGeneratedSource(result, "OrderDto.Mapping");
        mapping.Should().Contain("CustomerId = entity.CustomerId");
        mapping.Should().NotContain("entity.Customer?.Id");
    }

    /// <summary>The control: the explicit path already resolved it.</summary>
    [Fact]
    public void TheExplicitPath_AlreadyDid()
    {
        var result = RunGenerator(Source("""[MapProperty("Customer.Name")] public string CustomerName { get; init; } = "";"""));

        GetDiagnosticsById(result, "PRAG0303").Should().BeEmpty();
        GetDiagnosticsById(result, "PRAG0302").Should().BeEmpty();
    }
}
