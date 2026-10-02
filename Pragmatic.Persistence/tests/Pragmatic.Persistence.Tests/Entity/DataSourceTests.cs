using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query;

namespace Pragmatic.Persistence.Tests.Entity;

public sealed class DataSourceTests
{
    // --- QueryStrategy ---

    [Fact]
    public void QueryStrategy_HasAllValues()
    {
        Enum.GetValues<QueryStrategy>().Should().HaveCount(4);
    }

    // --- QueryStrategyAttribute ---

    [Fact]
    public void QueryStrategyAttribute_DefaultStrategy_IsEntity()
    {
        var attr = new QueryStrategyAttribute();
        attr.Strategy.Should().Be(QueryStrategy.Entity);
    }

    [Fact]
    public void QueryStrategyAttribute_CanSetStrategy()
    {
        var attr = new QueryStrategyAttribute { Strategy = QueryStrategy.Projection };
        attr.Strategy.Should().Be(QueryStrategy.Projection);
    }

    [Fact]
    public void QueryStrategyAttribute_TargetsClass()
    {
        var usage = typeof(QueryStrategyAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    // --- LoadWithAttribute ---

    [Fact]
    public void LoadWithAttribute_DefaultMaxDepth_IsOne()
    {
        var attr = new LoadWithAttribute<TestEntity>();
        attr.MaxDepth.Should().Be(1);
    }

    [Fact]
    public void LoadWithAttribute_SplitQuery_DefaultsFalse()
    {
        var attr = new LoadWithAttribute<TestEntity>();
        attr.SplitQuery.Should().BeFalse();
    }

    [Fact]
    public void LoadWithAttribute_CanSetProperties()
    {
        var attr = new LoadWithAttribute<TestEntity> { MaxDepth = 3, SplitQuery = true };
        attr.MaxDepth.Should().Be(3);
        attr.SplitQuery.Should().BeTrue();
    }

    // --- PolymorphicAttachmentAttribute ---

    [Fact]
    public void PolymorphicAttachmentAttribute_TargetsClass()
    {
        var usage = typeof(PolymorphicAttachmentAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    // --- AttachableAttribute ---

    [Fact]
    public void AttachableAttribute_AllowsMultiple()
    {
        var usage = typeof(AttachableAttribute<TestEntity>)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeTrue();
    }

    // --- InheritanceMapping ---

    [Fact]
    public void InheritanceStrategy_HasAllValues()
    {
        Enum.GetValues<InheritanceStrategy>().Should().HaveCount(3);
    }

    [Fact]
    public void InheritanceAttribute_StoresStrategy()
    {
        var attr = new InheritanceAttribute(InheritanceStrategy.Tph);
        attr.Strategy.Should().Be(InheritanceStrategy.Tph);
    }

    [Fact]
    public void InheritanceAttribute_DiscriminatorColumn_DefaultsToDiscriminator()
    {
        var attr = new InheritanceAttribute(InheritanceStrategy.Tph);
        attr.DiscriminatorColumn.Should().Be("Discriminator");
    }

    // --- CascadeOnAttribute ---

    [Fact]
    public void CascadeOnAttribute_StoresSourceProperty()
    {
        var attr = new CascadeOnAttribute<TestEntity>("Price");
        attr.SourceProperty.Should().Be("Price");
    }

    [Fact]
    public void CascadeOnAttribute_Condition_DefaultsNull()
    {
        var attr = new CascadeOnAttribute<TestEntity>("Price");
        attr.Condition.Should().BeNull();
    }

    [Fact]
    public void CascadeOnAttribute_Condition_CanBeSet()
    {
        var attr = new CascadeOnAttribute<TestEntity>("Price") { Condition = "IsPending" };
        attr.Condition.Should().Be("IsPending");
    }

    // --- CTE Attributes ---

    [Fact]
    public void GenerateTimelineAttribute_TargetsClass()
    {
        var usage = typeof(GenerateTimelineAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void GenerateHierarchyAttribute_TargetsClass()
    {
        var usage = typeof(GenerateHierarchyAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    #region Test Helpers

    private sealed class TestEntity;

    #endregion
}
