namespace Pragmatic.Tags.Tests;

public class EntityTagBaseTests
{
    private sealed class GuidEntityTag : EntityTagBase<Guid>
    {
    }

    private sealed class IntEntityTag : EntityTagBase<int>
    {
    }

    private sealed class StringEntityTag : EntityTagBase<string>
    {
    }

    [Fact]
    public void Defaults_TagId_IsEmptyGuid()
    {
        new GuidEntityTag().TagId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Defaults_AddedAt_IsDefault()
    {
        new GuidEntityTag().AddedAt.Should().Be(default);
    }

    [Fact]
    public void Defaults_AddedBy_IsNull()
    {
        new GuidEntityTag().AddedBy.Should().BeNull();
    }

    [Fact]
    public void Defaults_ParentEntityId_Guid_IsEmptyGuid()
    {
        new GuidEntityTag().ParentEntityId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Defaults_ParentEntityId_Int_IsZero()
    {
        new IntEntityTag().ParentEntityId.Should().Be(0);
    }

    [Fact]
    public void Defaults_ParentEntityId_String_IsNullViaDefaultBang()
    {
        // ParentEntityId is initialized to default! — for a reference type that is null.
        new StringEntityTag().ParentEntityId.Should().BeNull();
    }

    [Fact]
    public void Properties_GuidKey_AreRoundTrippable()
    {
        var parentId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var addedAt = DateTimeOffset.UtcNow;

        var junction = new GuidEntityTag
        {
            ParentEntityId = parentId,
            TagId = tagId,
            AddedAt = addedAt,
            AddedBy = "bob",
        };

        junction.ParentEntityId.Should().Be(parentId);
        junction.TagId.Should().Be(tagId);
        junction.AddedAt.Should().Be(addedAt);
        junction.AddedBy.Should().Be("bob");
    }

    [Fact]
    public void Properties_IntKey_AreRoundTrippable()
    {
        var junction = new IntEntityTag { ParentEntityId = 42, TagId = Guid.NewGuid() };

        junction.ParentEntityId.Should().Be(42);
    }

    [Fact]
    public void Properties_StringKey_AreRoundTrippable()
    {
        var junction = new StringEntityTag { ParentEntityId = "parent-key", TagId = Guid.NewGuid() };

        junction.ParentEntityId.Should().Be("parent-key");
    }

    [Fact]
    public void Type_IsAbstract()
    {
        typeof(EntityTagBase<>).IsAbstract.Should().BeTrue();
    }

    [Fact]
    public void Type_IsGeneric_WithSingleParameter()
    {
        typeof(EntityTagBase<>).IsGenericTypeDefinition.Should().BeTrue();
        typeof(EntityTagBase<>).GetGenericArguments().Should().ContainSingle();
    }
}
