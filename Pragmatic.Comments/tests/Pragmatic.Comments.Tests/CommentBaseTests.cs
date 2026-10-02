using Pragmatic.Testing.Assertions;
using Pragmatic.Comments;

namespace Pragmatic.Comments.Tests;

public class CommentBaseTests
{
    [Fact]
    public void Id_And_PersistenceId_AreSynced()
    {
        var comment = new TestComment();
        var id = Guid.NewGuid();

        comment.Id = id;

        comment.PersistenceId.Should().Be(id);
    }

    [Fact]
    public void PersistenceId_Sets_Id()
    {
        var comment = new TestComment();
        var id = Guid.NewGuid();

        comment.PersistenceId = id;

        comment.Id.Should().Be(id);
    }

    [Fact]
    public void Default_Status_IsVisible()
    {
        var comment = new TestComment();

        comment.Status.Should().Be(CommentStatus.Visible);
    }

    [Fact]
    public void Default_Visibility_IsPublic()
    {
        var comment = new TestComment();

        comment.Visibility.Should().Be(CommentVisibility.Public);
    }

    [Fact]
    public void Default_IsDeleted_IsFalse()
    {
        var comment = new TestComment();

        comment.IsDeleted.Should().BeFalse();
        comment.IsEdited.Should().BeFalse();
    }

    [Fact]
    public void SoftDelete_SetsFields()
    {
        var comment = new TestComment();
        var now = DateTimeOffset.UtcNow;

        comment.IsDeleted = true;
        comment.DeletedAt = now;
        comment.DeletedBy = "admin";

        comment.IsDeleted.Should().BeTrue();
        comment.DeletedAt.Should().Be(now);
        comment.DeletedBy.Should().Be("admin");
    }

    private sealed class TestComment : CommentBase<Guid>
    {
    }
}
