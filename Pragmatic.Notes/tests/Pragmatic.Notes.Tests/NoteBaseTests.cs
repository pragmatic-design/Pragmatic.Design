using Pragmatic.Persistence.Entity;

namespace Pragmatic.Notes.Tests;

public class NoteBaseTests
{
    private sealed class ConcreteNote : NoteBase<Guid>
    {
    }

    [Fact]
    public void Defaults_Id_IsEmptyGuid()
    {
        new ConcreteNote().Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Defaults_ParentEntityId_IsDefault()
    {
        new ConcreteNote().ParentEntityId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Defaults_Content_IsEmptyString()
    {
        new ConcreteNote().Content.Should().BeEmpty();
    }

    [Fact]
    public void Defaults_AuthorId_IsEmptyString()
    {
        new ConcreteNote().AuthorId.Should().BeEmpty();
    }

    [Fact]
    public void Defaults_AuthorName_IsEmptyString()
    {
        new ConcreteNote().AuthorName.Should().BeEmpty();
    }

    [Fact]
    public void Defaults_IsEdited_IsFalse()
    {
        new ConcreteNote().IsEdited.Should().BeFalse();
    }

    [Fact]
    public void Defaults_CreatedAt_IsDefault()
    {
        new ConcreteNote().CreatedAt.Should().Be(default);
    }

    [Fact]
    public void Defaults_UpdatedAt_IsNull()
    {
        new ConcreteNote().UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Defaults_UpdatedBy_IsNull()
    {
        new ConcreteNote().UpdatedBy.Should().BeNull();
    }

    [Fact]
    public void Defaults_IsDeleted_IsFalse()
    {
        new ConcreteNote().IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Defaults_DeletedAt_IsNull()
    {
        new ConcreteNote().DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Defaults_DeletedBy_IsNull()
    {
        new ConcreteNote().DeletedBy.Should().BeNull();
    }

    [Fact]
    public void PersistenceId_ForwardsToId_OnGet()
    {
        var id = Guid.NewGuid();
        var note = new ConcreteNote { Id = id };

        note.PersistenceId.Should().Be(id);
    }

    [Fact]
    public void PersistenceId_ForwardsToId_OnSet()
    {
        var id = Guid.NewGuid();
        var note = new ConcreteNote { PersistenceId = id };

        note.Id.Should().Be(id);
    }

    [Fact]
    public void PersistenceId_AndId_StayInSync()
    {
        var note = new ConcreteNote();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        note.Id = first;
        note.PersistenceId.Should().Be(first);

        note.PersistenceId = second;
        note.Id.Should().Be(second);
    }

    [Fact]
    public void Properties_AreRoundTrippable()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var updatedAt = createdAt.AddMinutes(5);
        var deletedAt = createdAt.AddMinutes(10);
        var parentId = Guid.NewGuid();

        var note = new ConcreteNote
        {
            ParentEntityId = parentId,
            Content = "Guest requested late check-out.",
            AuthorId = "user-42",
            AuthorName = "Alice",
            IsEdited = true,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            UpdatedBy = "user-43",
            IsDeleted = true,
            DeletedAt = deletedAt,
            DeletedBy = "user-44",
        };

        note.ParentEntityId.Should().Be(parentId);
        note.Content.Should().Be("Guest requested late check-out.");
        note.AuthorId.Should().Be("user-42");
        note.AuthorName.Should().Be("Alice");
        note.IsEdited.Should().BeTrue();
        note.CreatedAt.Should().Be(createdAt);
        note.UpdatedAt.Should().Be(updatedAt);
        note.UpdatedBy.Should().Be("user-43");
        note.IsDeleted.Should().BeTrue();
        note.DeletedAt.Should().Be(deletedAt);
        note.DeletedBy.Should().Be("user-44");
    }

    [Fact]
    public void ParentEntityId_SupportsNonGuidKeyType()
    {
        var note = new IntKeyedNote { ParentEntityId = 7 };

        note.ParentEntityId.Should().Be(7);
    }

    [Fact]
    public void Type_IsAbstract()
    {
        typeof(NoteBase<Guid>).IsAbstract.Should().BeTrue();
    }

    [Fact]
    public void Type_ImplementsIEntityOfGuid()
    {
        typeof(NoteBase<Guid>).Should().BeAssignableTo<IEntity>();
        new ConcreteNote().Should().BeAssignableTo<IEntity>();
    }

    [Fact]
    public void Type_ImplementsISoftDelete()
    {
        typeof(NoteBase<Guid>).Should().BeAssignableTo<ISoftDelete>();
        new ConcreteNote().Should().BeAssignableTo<ISoftDelete>();
    }

    private sealed class IntKeyedNote : NoteBase<int>
    {
    }
}
