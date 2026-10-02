using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Showcase.Billing.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests for LineItem entity with [CascadeOn] and [DefaultValue] attributes.
/// </summary>
public class LineItemCascadeTests
{
    [Fact]
    public void Create_DefaultQuantity_IsOne()
    {
        var item = LineItem.Create(150m, 150m);

        item.Quantity.Should().Be(1);
    }

    [Fact]
    public void Create_SetsUnitPrice()
    {
        var item = LineItem.Create(150m, 150m);

        item.UnitPrice.Should().Be(150m);
    }

    [Fact]
    public void SetUnitPrice_UpdatesValue()
    {
        var item = LineItem.Create(150m, 150m);
        item.SetUnitPrice(175m);

        item.UnitPrice.Should().Be(175m);
    }

    [Fact]
    public void SetRoomTypeId_LinksToRoomType()
    {
        var roomTypeId = Guid.NewGuid();
        var item = LineItem.Create(150m, 150m);
        item.SetRoomTypeId(roomTypeId);

        item.RoomTypeId.Should().Be(roomTypeId);
    }

    [Fact]
    public void HasCascadeOnAttribute_OnUnitPrice()
    {
        var prop = typeof(LineItem).GetProperty(nameof(LineItem.UnitPrice));

        var attrs = prop!.GetCustomAttributes(false);
        attrs.Should().ContainSingle(a => a.GetType().Name.Contains("CascadeOnAttribute"));
    }

    [Fact]
    public void HasDefaultValueAttribute_OnQuantity()
    {
        var prop = typeof(LineItem).GetProperty(nameof(LineItem.Quantity));

        var attr = prop!.GetCustomAttributes(typeof(DefaultValueAttribute), false);
        attr.Should().ContainSingle();
    }

    [Fact]
    public void TheInvoiceKey_IsTheRelations_WrittenThroughItsSetter()
    {
        // The key is generated from [Relation.ManyToOne<Invoice>], not a factory parameter: it is
        // written by EF when the item joins the invoice's collection, or explicitly through the
        // generated setter.
        var invoiceId = Guid.NewGuid();
        var item = LineItem.Create(100m, 200m);
        item.SetInvoiceId(invoiceId);

        item.InvoiceId.Should().Be(invoiceId);
    }
}
