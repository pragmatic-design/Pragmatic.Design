using Conformance.Poco.Dtos;
using Conformance.Poco.Shapes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Poco.Tests.Cases;

/// <summary>
///     Reading: from object to DTO.
/// </summary>
/// <remarks>
///     Between plain objects there is nothing to load, so these cases have no database and no setup:
///     a read is a copy.
/// </remarks>
public class ReadingShapes
{
    private static Basket ABasketWithTwoItems() => new()
    {
        Id = 1,
        Label = "groceries",
        Owner = new BasketOwner { Id = 7, Name = "Mario" },
        Items =
        [
            new BasketItem
            {
                Id = 10, Product = "bread", Quantity = 2,
                Tags = [new ItemTag { Id = 100, Name = "bakery" }]
            },
            new BasketItem { Id = 11, Product = "milk", Quantity = 1, Tags = [] }
        ]
    };

    /// <summary>The root's scalars reach the DTO.</summary>
    [Fact]
    public void FromEntity_CopiesTheScalars()
    {
        var dto = BasketDto.FromEntity(ABasketWithTwoItems());

        dto.Id.Should().Be(1, "a read copies the identity");
        dto.Label.Should().Be("groceries");
    }

    /// <summary>The read descends into the children and the children's children.</summary>
    [Fact]
    public void FromEntity_DescendsTwoLevels()
    {
        var dto = BasketDto.FromEntity(ABasketWithTwoItems());

        dto.Items.Should().HaveCount(2, "the first level");
        dto.Items[0].Tags.Should().HaveCount(1, "the second, which is what makes the shape two deep");
        dto.Items[0].Tags[0].Name.Should().Be("bakery");
    }

    /// <summary>The single navigation is read too.</summary>
    [Fact]
    public void FromEntity_ReadsTheSingleNavigation()
    {
        var dto = BasketDto.FromEntity(ABasketWithTwoItems());

        dto.Owner.Should().NotBeNull();
        dto.Owner!.Name.Should().Be("Mario");
    }

    /// <summary>
    ///     ⚠️ The two navigation lists are deep and prefixed.
    /// </summary>
    /// <remarks>
    ///     They coincide here because the DTO's two shapes coincide; the case where they diverge needs a
    ///     DTO that reads from one navigation and writes to another, and is a case of its own.
    /// </remarks>
    [Fact]
    public void TheNavigationLists_AreDeepAndPrefixed()
    {
        BasketDto.RequiredNavigations.Should().BeEquivalentTo(
            ["Items", "Items.Tags", "Owner"],
            "the read descends through the nested DTOs and prefixes the path that reaches them");

        BasketDto.WrittenNavigations.Should().BeEquivalentTo(
            ["Items", "Items.Tags", "Owner"],
            "and the write does the same, on its own model");
    }

    /// <summary>A leaf publishes empty lists, and publishes them all the same.</summary>
    /// <remarks>
    ///     They are emitted even when empty, because a consumer can only write their name and cannot
    ///     check that they exist.
    /// </remarks>
    [Fact]
    public void ALeaf_StillPublishesBothLists()
    {
        ItemTagDto.RequiredNavigations.Should().BeEmpty();
        ItemTagDto.WrittenNavigations.Should().BeEmpty();
    }
}
