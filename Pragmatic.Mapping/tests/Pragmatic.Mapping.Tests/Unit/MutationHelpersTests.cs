using Pragmatic.Testing.Assertions;
using Pragmatic.Mapping.Mutation;
using Xunit;

namespace Pragmatic.Mapping.Tests.Unit;

/// <summary>
///     Tests for <see cref="MutationHelpers" /> — composable mutation helpers
///     for MapOneToOne and MapOneToMany with collection strategies.
/// </summary>
public class MutationHelpersTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Test Models
    // ═══════════════════════════════════════════════════════════════════════════

    private class AddressDto
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
    }

    private class Address
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
    }

    private class LineDto
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
    }

    private class OrderLine
    {
        public int Id { get; set; }
        public string Product { get; set; } = "";
        public int Quantity { get; set; }
    }

    private class Order
    {
        public Address? ShippingAddress { get; set; }
        public List<OrderLine> Lines { get; set; } = [];
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MapOneToOne Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOneToOne_WithNullDto_SetsNavigationToNull()
    {
        var order = new Order { ShippingAddress = new Address { Street = "Old St", City = "Old City" } };

        MutationHelpers.MapOneToOne<AddressDto, Address>(
            dtoValue: null,
            existingGetter: () => order.ShippingAddress,
            setter: addr => order.ShippingAddress = addr,
            factory: dto => new Address { Street = dto.Street, City = dto.City });

        order.ShippingAddress.Should().BeNull();
    }

    [Fact]
    public void MapOneToOne_WithDtoAndNoExisting_CreatesNewEntity()
    {
        var order = new Order { ShippingAddress = null };
        var dto = new AddressDto { Street = "New St", City = "New City" };

        MutationHelpers.MapOneToOne(
            dtoValue: dto,
            existingGetter: () => order.ShippingAddress,
            setter: addr => order.ShippingAddress = addr,
            factory: d => new Address { Street = d.Street, City = d.City });

        order.ShippingAddress.Should().NotBeNull();
        order.ShippingAddress!.Street.Should().Be("New St");
        order.ShippingAddress.City.Should().Be("New City");
    }

    [Fact]
    public void MapOneToOne_WithDtoAndExisting_UpdatesInPlace()
    {
        var existingAddress = new Address { Street = "Old St", City = "Old City" };
        var order = new Order { ShippingAddress = existingAddress };
        var dto = new AddressDto { Street = "Updated St", City = "Updated City" };

        MutationHelpers.MapOneToOne(
            dtoValue: dto,
            existingGetter: () => order.ShippingAddress,
            setter: addr => order.ShippingAddress = addr,
            factory: d => new Address { Street = d.Street, City = d.City },
            updater: (d, e) => { e.Street = d.Street; e.City = d.City; });

        // Should update in-place, not replace
        order.ShippingAddress.Should().BeSameAs(existingAddress);
        order.ShippingAddress!.Street.Should().Be("Updated St");
        order.ShippingAddress.City.Should().Be("Updated City");
    }

    [Fact]
    public void MapOneToOne_WithDtoAndExisting_NoUpdater_CreatesNew()
    {
        var existingAddress = new Address { Street = "Old St", City = "Old City" };
        var order = new Order { ShippingAddress = existingAddress };
        var dto = new AddressDto { Street = "New St", City = "New City" };

        MutationHelpers.MapOneToOne(
            dtoValue: dto,
            existingGetter: () => order.ShippingAddress,
            setter: addr => order.ShippingAddress = addr,
            factory: d => new Address { Street = d.Street, City = d.City });

        // Without updater, should create new (factory is used since updater is null)
        order.ShippingAddress.Should().NotBeSameAs(existingAddress);
        order.ShippingAddress!.Street.Should().Be("New St");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MapOneToMany — Sync Strategy Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOneToMany_Sync_AddsNewItems()
    {
        var order = new Order();
        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 5 },
            new() { Id = 2, Product = "Gadget", Quantity = 3 }
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.Sync);

        order.Lines.Should().HaveCount(2);
        order.Lines.Select(l => l.Product).Should().Contain("Widget").And.Contain("Gadget");
    }

    [Fact]
    public void MapOneToMany_Sync_UpdatesExistingItems()
    {
        var existingLine = new OrderLine { Id = 1, Product = "Widget", Quantity = 5 };
        var order = new Order { Lines = [existingLine] };
        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 10 }
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            updater: (d, e) => { e.Product = d.Product; e.Quantity = d.Quantity; },
            strategy: CollectionStrategy.Sync);

        order.Lines.Should().HaveCount(1);
        order.Lines[0].Should().BeSameAs(existingLine);
        order.Lines[0].Quantity.Should().Be(10);
    }

    [Fact]
    public void MapOneToMany_Sync_RemovesMissingItems()
    {
        var order = new Order
        {
            Lines =
            [
                new OrderLine { Id = 1, Product = "Widget", Quantity = 5 },
                new OrderLine { Id = 2, Product = "Gadget", Quantity = 3 },
                new OrderLine { Id = 3, Product = "Doohickey", Quantity = 1 }
            ]
        };

        // DTO only has item 1 — items 2 and 3 should be removed
        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 5 }
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.Sync);

        order.Lines.Should().HaveCount(1);
        order.Lines[0].Id.Should().Be(1);
    }

    [Fact]
    public void MapOneToMany_Sync_AddUpdateRemoveCombined()
    {
        var keepLine = new OrderLine { Id = 1, Product = "Widget", Quantity = 5 };
        var order = new Order
        {
            Lines =
            [
                keepLine,
                new OrderLine { Id = 2, Product = "ToRemove", Quantity = 1 }
            ]
        };

        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 99 }, // Update
            new() { Id = 3, Product = "NewItem", Quantity = 7 }  // Add
            // Id=2 is missing → removed
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            updater: (d, e) => { e.Product = d.Product; e.Quantity = d.Quantity; },
            strategy: CollectionStrategy.Sync);

        order.Lines.Should().HaveCount(2);
        order.Lines.Should().Contain(l => l.Id == 1 && l.Quantity == 99);
        order.Lines.Should().Contain(l => l.Id == 3 && l.Product == "NewItem");
        order.Lines.Should().NotContain(l => l.Id == 2);
        // Item 1 should be updated in-place
        order.Lines.First(l => l.Id == 1).Should().BeSameAs(keepLine);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MapOneToMany — AddOnly Strategy Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOneToMany_AddOnly_AddsNewAndUpdatesExisting()
    {
        var existingLine = new OrderLine { Id = 1, Product = "Widget", Quantity = 5 };
        var order = new Order { Lines = [existingLine] };

        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 99 }, // Update
            new() { Id = 2, Product = "NewItem", Quantity = 3 }  // Add
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            updater: (d, e) => { e.Product = d.Product; e.Quantity = d.Quantity; },
            strategy: CollectionStrategy.AddOnly);

        order.Lines.Should().HaveCount(2);
        order.Lines.First(l => l.Id == 1).Quantity.Should().Be(99);
        order.Lines.Should().Contain(l => l.Id == 2);
    }

    [Fact]
    public void MapOneToMany_AddOnly_DoesNotRemoveMissingItems()
    {
        var order = new Order
        {
            Lines =
            [
                new OrderLine { Id = 1, Product = "Widget", Quantity = 5 },
                new OrderLine { Id = 2, Product = "Gadget", Quantity = 3 }
            ]
        };

        // DTO only has item 1 — item 2 should NOT be removed
        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 5 }
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.AddOnly);

        order.Lines.Should().HaveCount(2);
        order.Lines.Should().Contain(l => l.Id == 2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MapOneToMany — Replace Strategy Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOneToMany_Replace_ClearsAndRecreates()
    {
        var order = new Order
        {
            Lines =
            [
                new OrderLine { Id = 1, Product = "Old1", Quantity = 1 },
                new OrderLine { Id = 2, Product = "Old2", Quantity = 2 }
            ]
        };

        var dtoLines = new List<LineDto>
        {
            new() { Id = 10, Product = "New1", Quantity = 5 },
            new() { Id = 11, Product = "New2", Quantity = 3 },
            new() { Id = 12, Product = "New3", Quantity = 7 }
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.Replace);

        order.Lines.Should().HaveCount(3);
        order.Lines.Select(l => l.Id).Should().BeEquivalentTo([10, 11, 12]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MapOneToMany — Ignore Strategy Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOneToMany_Ignore_LeavesTheCollectionUntouched()
    {
        var order = new Order
        {
            Lines = [new OrderLine { Id = 1, Product = "Widget", Quantity = 5 }]
        };

        MutationHelpers.MapOneToMany(
            dtoItems: new List<LineDto> { new() { Id = 2, Product = "Gadget", Quantity = 9 } },
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.Ignore);

        order.Lines.Should().HaveCount(1);
        order.Lines.Single().Product.Should().Be("Widget");
    }

    /// <summary>
    ///     Ignore returns before the guards: demanding the arguments it will never use would be theatre.
    /// </summary>
    [Fact]
    public void MapOneToMany_Ignore_DoesNotDemandTheArgumentsItNeverUses()
    {
        var order = new Order { Lines = [] };

        var act = () => MutationHelpers.MapOneToMany<LineDto, OrderLine, int>(
            dtoItems: null,
            entityCollection: order.Lines,
            dtoKeySelector: null!,
            entityKeySelector: null!,
            factory: null!,
            strategy: CollectionStrategy.Ignore);

        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOneToMany_WithNullDtoItems_TreatsAsEmpty()
    {
        var order = new Order
        {
            Lines = [new OrderLine { Id = 1, Product = "Widget", Quantity = 5 }]
        };

        MutationHelpers.MapOneToMany<LineDto, OrderLine, int>(
            dtoItems: null,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.Sync);

        // Sync with empty → removes all existing
        order.Lines.Should().BeEmpty();
    }

    [Fact]
    public void MapOneToMany_WithEmptyEntityCollection_AddsAll()
    {
        var order = new Order();

        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "A", Quantity = 1 },
            new() { Id = 2, Product = "B", Quantity = 2 }
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.Sync);

        order.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void MapOneToMany_Replace_WithNullDtoItems_ClearsCollection()
    {
        var order = new Order
        {
            Lines = [new OrderLine { Id = 1, Product = "Widget", Quantity = 5 }]
        };

        MutationHelpers.MapOneToMany<LineDto, OrderLine, int>(
            dtoItems: null,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: CollectionStrategy.Replace);

        order.Lines.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Duplicate-key hardening
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(CollectionStrategy.Sync)]
    [InlineData(CollectionStrategy.AddOnly)]
    public void MapOneToMany_DuplicateDtoKeys_ThrowsDuplicateMappingKeyException(CollectionStrategy strategy)
    {
        var order = new Order();
        var dtoLines = new List<LineDto>
        {
            new() { Id = 7, Product = "Widget", Quantity = 5 },
            new() { Id = 7, Product = "Widget-again", Quantity = 9 }
        };

        var act = () => MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            strategy: strategy);

        act.Should().Throw<DuplicateMappingKeyException>()
            .Which.Side.Should().Be("DTO");

        // The collection must not have been partially mutated with duplicate children.
        order.Lines.Should().NotContain(l => l.Product == "Widget-again");
    }

    [Theory]
    [InlineData(CollectionStrategy.Sync)]
    [InlineData(CollectionStrategy.AddOnly)]
    public void MapOneToMany_DuplicateEntityKeys_ThrowsClearDomainException(CollectionStrategy strategy)
    {
        // Two existing entities collide on key 1 — a naive lookup would throw a raw ArgumentException
        // from ToDictionary (AddOnly) or silently overwrite the lookup (Sync).
        var order = new Order
        {
            Lines =
            [
                new OrderLine { Id = 1, Product = "Widget", Quantity = 5 },
                new OrderLine { Id = 1, Product = "Duplicate", Quantity = 6 }
            ]
        };

        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 10 }
        };

        var act = () => MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            updater: (d, e) => { e.Product = d.Product; e.Quantity = d.Quantity; },
            strategy: strategy);

        act.Should().Throw<DuplicateMappingKeyException>()
            .Which.Side.Should().Be("entity");
    }

    [Fact]
    public void MapOneToMany_Sync_UniqueKeys_StillSyncsNormally()
    {
        // Guard against over-zealous duplicate detection: a normal unique-key sync must work.
        var existing = new OrderLine { Id = 1, Product = "Widget", Quantity = 5 };
        var order = new Order { Lines = [existing] };
        var dtoLines = new List<LineDto>
        {
            new() { Id = 1, Product = "Widget", Quantity = 50 },
            new() { Id = 2, Product = "Gadget", Quantity = 3 }
        };

        MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: d => d.Id,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id, Product = d.Product, Quantity = d.Quantity },
            updater: (d, e) => { e.Product = d.Product; e.Quantity = d.Quantity; },
            strategy: CollectionStrategy.Sync);

        order.Lines.Should().HaveCount(2);
        order.Lines.First(l => l.Id == 1).Should().BeSameAs(existing);
        order.Lines.First(l => l.Id == 1).Quantity.Should().Be(50);
    }

    // Required delegates are guarded (Ensure), so a null delegate throws a clean
    // ArgumentNullException instead of an NRE deep in the sync loop.
    [Fact]
    public void MapOneToOne_NullFactory_ThrowsArgumentNull()
    {
        var order = new Order { ShippingAddress = null };
        var dto = new AddressDto { Street = "S", City = "C" };

        var act = () => MutationHelpers.MapOneToOne(
            dtoValue: dto,
            existingGetter: () => order.ShippingAddress,
            setter: addr => order.ShippingAddress = addr,
            factory: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MapOneToMany_NullKeySelector_ThrowsArgumentNull()
    {
        var order = new Order();
        var dtoLines = new List<LineDto> { new() { Id = 1, Product = "W", Quantity = 1 } };

        var act = () => MutationHelpers.MapOneToMany(
            dtoItems: dtoLines,
            entityCollection: order.Lines,
            dtoKeySelector: null!,
            entityKeySelector: e => e.Id,
            factory: d => new OrderLine { Id = d.Id });

        act.Should().Throw<ArgumentNullException>();
    }
}
