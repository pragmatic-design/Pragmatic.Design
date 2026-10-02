using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Showcase.Billing.Entities;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Lifecycle;

/// <summary>
/// Tests verifying [DefaultValue] attribute integration in Showcase entities.
/// The SG generates initializers in the Create() factory for these.
/// </summary>
public class DefaultValueTests
{
    [Fact]
    public void Category_Level_HasDefaultValueAttribute()
    {
        var prop = typeof(Category).GetProperty(nameof(Category.Level));
        var attr = prop!.GetCustomAttributes(typeof(DefaultValueAttribute), false);

        attr.Should().ContainSingle();
        ((DefaultValueAttribute)attr[0]).Value.Should().Be(0);
    }

    [Fact]
    public void LineItem_Quantity_HasDefaultValueAttribute()
    {
        var prop = typeof(LineItem).GetProperty(nameof(LineItem.Quantity));
        var attr = prop!.GetCustomAttributes(typeof(DefaultValueAttribute), false);

        attr.Should().ContainSingle();
        ((DefaultValueAttribute)attr[0]).Value.Should().Be(1);
    }

    [Fact]
    public void Category_Create_AppliesDefaultLevel()
    {
        var category = Category.Create();

        // DefaultValue(0) should be applied by the generated Create() factory
        category.Level.Should().Be(0);
    }

    [Fact]
    public void LineItem_Create_AppliesDefaultQuantity()
    {
        var item = LineItem.Create(10m, 10m);

        // DefaultValue(1) should be applied by the generated Create() factory
        item.Quantity.Should().Be(1);
    }
}
