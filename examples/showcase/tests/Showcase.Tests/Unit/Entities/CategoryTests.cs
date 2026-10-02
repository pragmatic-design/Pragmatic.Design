using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests for the Category hierarchical entity.
/// Demonstrates [GenerateHierarchy] and [DefaultValue] usage.
/// </summary>
public class CategoryTests
{
    [Fact]
    public void Create_SetsDefaultLevel_ToZero()
    {
        var category = CreateCategory("Resorts");

        category.Level.Should().Be(0);
    }

    [Fact]
    public void Create_GeneratesGuidId()
    {
        var category = CreateCategory("City Hotels");

        category.PersistenceId.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_DefaultPath_IsRoot()
    {
        var category = CreateCategory("Luxury");

        category.Path.Should().Be("/");
    }

    [Fact]
    public void Create_ParentId_IsNull_ForRootCategory()
    {
        var category = CreateCategory("All Hotels");

        category.ParentId.Should().BeNull();
    }

    [Fact]
    public void SetParentId_AssignsParent()
    {
        var parent = CreateCategory("Resorts");
        var child = CreateCategory("Beach Resorts");

        child.SetParentId(parent.Id);

        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public void SetLevel_UpdatesHierarchyDepth()
    {
        var category = CreateCategory("Sub-Category");

        category.SetLevel(2);

        category.Level.Should().Be(2);
    }

    [Fact]
    public void SetPath_UpdatesMaterializedPath()
    {
        var category = CreateCategory("Beach Resorts");

        category.SetPath("/resorts/beach-resorts");

        category.Path.Should().Be("/resorts/beach-resorts");
    }

    [Fact]
    public void SoftDelete_DefaultValues()
    {
        var category = CreateCategory("Temp");

        category.IsDeleted.Should().BeFalse();
        category.DeletedAt.Should().BeNull();
        category.DeletedBy.Should().BeNull();
    }

    /// <summary>
    /// Helper: Create() has no params (Name is private-set, not required in factory).
    /// Set name via generated setter.
    /// </summary>
    private static Category CreateCategory(string name)
    {
        var category = Category.Create();
        category.SetName(name);
        return category;
    }
}
