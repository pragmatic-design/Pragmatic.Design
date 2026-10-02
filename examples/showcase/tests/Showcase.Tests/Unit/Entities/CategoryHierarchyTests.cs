using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests that the [GenerateHierarchy] on Category generates the hierarchy CTE extensions.
/// </summary>
public class CategoryHierarchyTests
{
    [Fact]
    public void CategoryHierarchyExtensions_ClassExists()
    {
        var type = typeof(Category).Assembly.GetType("Showcase.Catalog.Entities.CategoryHierarchyExtensions");
        type.Should().NotBeNull("SG should generate CategoryHierarchyExtensions");
    }

    [Fact]
    public void CategoryHierarchyExtensions_HasGetDescendants()
    {
        var type = typeof(Category).Assembly.GetType("Showcase.Catalog.Entities.CategoryHierarchyExtensions");
        type.Should().NotBeNull();

        var method = type!.GetMethod("GetDescendantsByParent");
        method.Should().NotBeNull("GetDescendants extension method should exist");
        method!.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void CategoryHierarchyExtensions_HasGetAncestors()
    {
        var type = typeof(Category).Assembly.GetType("Showcase.Catalog.Entities.CategoryHierarchyExtensions");
        type.Should().NotBeNull();

        var method = type!.GetMethod("GetAncestorsByParent");
        method.Should().NotBeNull("GetAncestors extension method should exist");
        method!.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void Category_HasParentId()
    {
        var category = new Category();
        typeof(Category).GetProperty("ParentId").Should().NotBeNull();
    }
}
