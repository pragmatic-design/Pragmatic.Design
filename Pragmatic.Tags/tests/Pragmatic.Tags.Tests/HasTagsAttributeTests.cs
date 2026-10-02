using System.Reflection;

namespace Pragmatic.Tags.Tests;

public class HasTagsAttributeTests
{
    [Fact]
    public void Defaults_MaxPerEntity_Is50()
    {
        new HasTagsAttribute().MaxPerEntity.Should().Be(50);
    }

    [Fact]
    public void Defaults_AllowCustom_IsTrue()
    {
        new HasTagsAttribute().AllowCustom.Should().BeTrue();
    }

    [Fact]
    public void Defaults_CaseSensitive_IsFalse()
    {
        new HasTagsAttribute().CaseSensitive.Should().BeFalse();
    }

    [Fact]
    public void Defaults_Scope_IsNull()
    {
        new HasTagsAttribute().Scope.Should().BeNull();
    }

    [Fact]
    public void Defaults_SubBoundary_IsNull()
    {
        new HasTagsAttribute().SubBoundary.Should().BeNull();
    }

    [Fact]
    public void Properties_AllOptions_AreSettable()
    {
        var attribute = new HasTagsAttribute
        {
            MaxPerEntity = 5,
            AllowCustom = false,
            CaseSensitive = true,
            Scope = "global",
            SubBoundary = "ArticleLabels",
        };

        attribute.MaxPerEntity.Should().Be(5);
        attribute.AllowCustom.Should().BeFalse();
        attribute.CaseSensitive.Should().BeTrue();
        attribute.Scope.Should().Be("global");
        attribute.SubBoundary.Should().Be("ArticleLabels");
    }

    [Fact]
    public void MaxPerEntity_Zero_RepresentsUnlimited()
    {
        new HasTagsAttribute { MaxPerEntity = 0 }.MaxPerEntity.Should().Be(0);
    }

    [Fact]
    public void Type_IsSealed()
    {
        typeof(HasTagsAttribute).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Type_DerivesFromAttribute()
    {
        typeof(HasTagsAttribute).Should().BeDerivedFrom<Attribute>();
    }

    [Fact]
    public void AttributeUsage_TargetsClassOnly()
    {
        var usage = typeof(HasTagsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void AttributeUsage_IsNotInherited()
    {
        var usage = typeof(HasTagsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.Inherited.Should().BeFalse();
    }

    [HasTags(MaxPerEntity = 3, AllowCustom = false, CaseSensitive = true, Scope = "shared", SubBoundary = "Sub")]
    private sealed class DecoratedEntity
    {
    }

    [Fact]
    public void Applied_AsAttribute_RoundTripsNamedArguments()
    {
        var attribute = typeof(DecoratedEntity).GetCustomAttribute<HasTagsAttribute>();

        attribute.Should().NotBeNull();
        attribute!.MaxPerEntity.Should().Be(3);
        attribute.AllowCustom.Should().BeFalse();
        attribute.CaseSensitive.Should().BeTrue();
        attribute.Scope.Should().Be("shared");
        attribute.SubBoundary.Should().Be("Sub");
    }
}
