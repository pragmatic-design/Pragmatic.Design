using System.Reflection;
using Pragmatic.Testing.Assertions;
using Pragmatic.Caching.Attributes;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

public class AttributeTests
{
    // --- CacheableAttribute ---

    [Fact]
    public void CacheableAttribute_DefaultDuration_Is5m()
    {
        var attr = new CacheableAttribute();

        attr.Duration.Should().Be("5m");
    }

    [Fact]
    public void CacheableAttribute_Sliding_DefaultIsFalse()
    {
        var attr = new CacheableAttribute();

        attr.Sliding.Should().BeFalse();
    }

    [Fact]
    public void CacheableAttribute_Priority_DefaultIsNormal()
    {
        var attr = new CacheableAttribute();

        attr.Priority.Should().Be(CachePriority.Normal);
    }

    [Fact]
    public void CacheableAttribute_Tags_DefaultIsNull()
    {
        var attr = new CacheableAttribute();

        attr.Tags.Should().BeNull();
    }

    [Fact]
    public void CacheableAttribute_TargetsClassOnly()
    {
        var usage = typeof(CacheableAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void CacheableAttribute_NotInherited()
    {
        var usage = typeof(CacheableAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage!.Inherited.Should().BeFalse();
    }

    [Fact]
    public void CacheableAttribute_SetDuration_Stored()
    {
        var attr = new CacheableAttribute { Duration = "1h" };

        attr.Duration.Should().Be("1h");
    }

    [Fact]
    public void CacheableAttribute_SetTags_Stored()
    {
        var attr = new CacheableAttribute { Tags = ["users", "tenant:{TenantId}"] };

        attr.Tags.Should().BeEquivalentTo("users", "tenant:{TenantId}");
    }

    // --- CacheKeyAttribute ---

    [Fact]
    public void CacheKeyAttribute_Exclude_DefaultIsFalse()
    {
        var attr = new CacheKeyAttribute();

        attr.Exclude.Should().BeFalse();
    }

    [Fact]
    public void CacheKeyAttribute_Order_DefaultIsMaxValue()
    {
        var attr = new CacheKeyAttribute();

        attr.Order.Should().Be(int.MaxValue);
    }

    [Fact]
    public void CacheKeyAttribute_Name_DefaultIsNull()
    {
        var attr = new CacheKeyAttribute();

        attr.Name.Should().BeNull();
    }

    [Fact]
    public void CacheKeyAttribute_TargetsPropertyOnly()
    {
        var usage = typeof(CacheKeyAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Property);
    }

    [Fact]
    public void CacheKeyAttribute_SetName_Stored()
    {
        var attr = new CacheKeyAttribute { Name = "id" };

        attr.Name.Should().Be("id");
    }

    [Fact]
    public void CacheKeyAttribute_SetExclude_Stored()
    {
        var attr = new CacheKeyAttribute { Exclude = true };

        attr.Exclude.Should().BeTrue();
    }

    // --- InvalidatesCacheAttribute ---

    [Fact]
    public void InvalidatesCacheAttribute_DefaultConstructor_EmptyTags()
    {
        var attr = new InvalidatesCacheAttribute();

        attr.Tags.Should().BeEmpty();
    }

    [Fact]
    public void InvalidatesCacheAttribute_WithTags_StoresTags()
    {
        var attr = new InvalidatesCacheAttribute("users", "profiles");

        attr.Tags.Should().BeEquivalentTo("users", "profiles");
    }

    [Fact]
    public void InvalidatesCacheAttribute_Keys_DefaultIsNull()
    {
        var attr = new InvalidatesCacheAttribute();

        attr.Keys.Should().BeNull();
    }

    [Fact]
    public void InvalidatesCacheAttribute_TargetsClassOnly()
    {
        var usage = typeof(InvalidatesCacheAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void InvalidatesCacheAttribute_NotInherited()
    {
        var usage = typeof(InvalidatesCacheAttribute)
            .GetCustomAttribute<AttributeUsageAttribute>();

        usage!.Inherited.Should().BeFalse();
    }

    [Fact]
    public void InvalidatesCacheAttribute_SingleTag_Stored()
    {
        var attr = new InvalidatesCacheAttribute("orders");

        attr.Tags.Should().ContainSingle().Which.Should().Be("orders");
    }
}
