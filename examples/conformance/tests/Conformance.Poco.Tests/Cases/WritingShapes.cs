using Conformance.Poco.Dtos;
using Conformance.Poco.Shapes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Poco.Tests.Cases;

/// <summary>
///     Writing: from DTO to object, creating and updating.
/// </summary>
/// <remarks>
///     ⚠️ Between plain objects there is no constraint on nesting: anything consistent between objects
///     holds. The constraint «a child must be written by a mutation, along a declared relation» belongs
///     to the mutation level and does not concern these cases.
/// </remarks>
public class WritingShapes
{
    /// <summary>
    ///     ⚠️ Without EF Core referenced, <c>ApplyTo(entity)</c> <b>is generated</b>.
    /// </summary>
    /// <remarks>
    ///     Not a runtime assertion: the proof is that this file <b>compiles</b>. If the behaviour changed,
    ///     this line would stop compiling — the strongest way to pin it, because it cannot be suppressed.
    /// </remarks>
    [Fact]
    public void WithoutEfCore_ThePlainApplyToExists()
    {
        var basket = new Basket { Id = 1, Label = "before" };

        new BasketDto { Id = 1, Label = "after" }.ApplyTo(basket);

        basket.Label.Should().Be("after");
    }

    /// <summary>Creation builds the whole graph, at every level.</summary>
    [Fact]
    public void ToEntity_BuildsTheWholeGraph()
    {
        var dto = new BasketDto
        {
            Label = "new",
            Owner = new BasketOwnerDto { Name = "Mario" },
            Items =
            [
                new BasketItemDto
                {
                    Product = "bread", Quantity = 2,
                    Tags = [new ItemTagDto { Name = "bakery" }]
                }
            ]
        };

        var basket = dto.ToEntity();

        basket.Label.Should().Be("new");
        basket.Items.Should().HaveCount(1);
        basket.Items[0].Tags.Should().HaveCount(1, "construction goes all the way down without loading anything");
        basket.Owner!.Name.Should().Be("Mario");
    }

    /// <summary>
    ///     ⚠️ The identity is never written, neither on create nor on update.
    /// </summary>
    /// <remarks><c>Id</c> addresses the row; it does not change it.</remarks>
    [Fact]
    public void TheIdentity_IsNeverWritten()
    {
        var built = new BasketDto { Id = 999, Label = "x" }.ToEntity();
        built.Id.Should().Be(0, "on create the DTO's Id does not reach the object");

        var existing = new Basket { Id = 42, Label = "before" };
        new BasketDto { Id = 999, Label = "after" }.ApplyTo(existing);
        existing.Id.Should().Be(42, "and on update it does not overwrite it");
    }

    /// <summary>
    ///     ⚠️ The central case: the collection is <b>merged by key</b>, not assigned.
    /// </summary>
    /// <remarks>
    ///     The three assertions are the three outcomes: present on both sides → updated in place, only in
    ///     the DTO → created, only in the object → removed.
    /// </remarks>
    [Fact]
    public void ApplyTo_MergesTheCollectionByKey()
    {
        var keep = new BasketItem { Id = 10, Product = "bread", Quantity = 1 };
        var drop = new BasketItem { Id = 11, Product = "milk", Quantity = 1 };
        var basket = new Basket { Id = 1, Label = "groceries", Items = [keep, drop] };

        new BasketDto
        {
            Id = 1,
            Label = "groceries",
            Items =
            [
                new BasketItemDto { Id = 10, Product = "bread", Quantity = 5 },   // exists
                new BasketItemDto { Id = 0, Product = "eggs", Quantity = 3 }      // new
            ]
        }.ApplyTo(basket);

        basket.Items.Should().HaveCount(2, "one updated, one created, one removed");

        basket.Items.Should().Contain(keep,
            "the row with a matching key is the SAME instance: merged, not rebuilt. "
            + "In a database that is what keeps its id, its audit columns and whatever points at it");
        keep.Quantity.Should().Be(5, "and its value was updated");

        basket.Items.Should().NotContain(drop, "what does not arrive goes — the Sync strategy");
        basket.Items.Should().Contain(i => i.Product == "eggs", "and what was not there is created");
    }

    /// <summary>The merge descends to the second level too.</summary>
    [Fact]
    public void ApplyTo_MergesTwoLevelsDown()
    {
        var tag = new ItemTag { Id = 100, Name = "bakery" };
        var item = new BasketItem { Id = 10, Product = "bread", Quantity = 1, Tags = [tag] };
        var basket = new Basket { Id = 1, Items = [item] };

        new BasketDto
        {
            Id = 1,
            Items =
            [
                new BasketItemDto
                {
                    Id = 10, Product = "bread", Quantity = 1,
                    Tags = [new ItemTagDto { Id = 100, Name = "baker's" }]
                }
            ]
        }.ApplyTo(basket);

        item.Tags.Should().ContainSingle().Which.Should().Be(tag,
            "two levels down the row is still the same instance");
        tag.Name.Should().Be("baker's", "and the value arrived");
    }

    /// <summary>The single navigation is merged: updated when present, built when absent.</summary>
    [Fact]
    public void ApplyTo_MergesTheSingleNavigation()
    {
        var owner = new BasketOwner { Id = 7, Name = "Mario" };
        var basket = new Basket { Id = 1, Owner = owner };

        new BasketDto { Id = 1, Owner = new BasketOwnerDto { Id = 7, Name = "Luigi" } }.ApplyTo(basket);

        basket.Owner.Should().Be(owner, "updated in place, not replaced");
        owner.Name.Should().Be("Luigi");

        var empty = new Basket { Id = 2 };
        new BasketDto { Id = 2, Owner = new BasketOwnerDto { Name = "New" } }.ApplyTo(empty);
        empty.Owner!.Name.Should().Be("New", "and built when absent");
    }

    /// <summary>
    ///     ⚠️ A <c>null</c> on the single navigation <b>leaves it alone</b>; it does not detach it.
    /// </summary>
    /// <remarks>
    ///     That is the reading of a partial update: «I am not telling you about this field», not
    ///     «remove it». Removing takes an operation that says so.
    /// </remarks>
    [Fact]
    public void ANullSingleNavigation_LeavesItAlone()
    {
        var owner = new BasketOwner { Id = 7, Name = "Mario" };
        var basket = new Basket { Id = 1, Owner = owner };

        new BasketDto { Id = 1, Label = "touched", Owner = null }.ApplyTo(basket);

        basket.Label.Should().Be("touched", "the rest of the write happened");
        basket.Owner.Should().Be(owner, "but the navigation stayed where it was");
    }
}
