using Pragmatic.Persistence.Entity;

namespace Pragmatic.Attachments.Tests;

/// <summary>
///     Unit tests for <see cref="AttachmentBase{TEntityId}"/>. The module shipped without any test
///     project at all, so nothing pinned the shape the SG-generated entity inherits.
/// </summary>
public class AttachmentBaseTests
{
    [Fact]
    public void Id_And_PersistenceId_AreSynced()
    {
        var id = Guid.NewGuid();
        var attachment = new TestAttachment { Id = id };

        attachment.PersistenceId.Should().Be(id);
    }

    [Fact]
    public void PersistenceId_Sets_Id()
    {
        var id = Guid.NewGuid();
        var attachment = new TestAttachment { PersistenceId = id };

        attachment.Id.Should().Be(id);
    }

    [Fact]
    public void Default_FileName_IsEmpty()
        => new TestAttachment().FileName.Should().BeEmpty();

    [Fact]
    public void Default_ContentType_IsEmpty()
        => new TestAttachment().ContentType.Should().BeEmpty();

    [Fact]
    public void Default_StorageUri_IsEmpty()
        => new TestAttachment().StorageUri.Should().BeEmpty();

    [Fact]
    public void Default_UploadedBy_IsEmpty()
        => new TestAttachment().UploadedBy.Should().BeEmpty();

    [Fact]
    public void Default_FileSize_IsZero()
        => new TestAttachment().FileSize.Should().Be(0);

    [Fact]
    public void Default_Description_IsNull()
        => new TestAttachment().Description.Should().BeNull();

    [Fact]
    public void Default_IsDeleted_IsFalse()
        => new TestAttachment().IsDeleted.Should().BeFalse();

    [Fact]
    public void SoftDelete_SetsFields()
    {
        var deletedAt = DateTimeOffset.UtcNow;
        var attachment = new TestAttachment
        {
            IsDeleted = true,
            DeletedAt = deletedAt,
            DeletedBy = "moderator",
        };

        attachment.IsDeleted.Should().BeTrue();
        attachment.DeletedAt.Should().Be(deletedAt);
        attachment.DeletedBy.Should().Be("moderator");
    }

    [Fact]
    public void Metadata_RoundTrips()
    {
        var uploadedAt = DateTimeOffset.UtcNow;
        var parentId = Guid.NewGuid();

        var attachment = new TestAttachment
        {
            ParentEntityId = parentId,
            FileName = "contract.pdf",
            FileSize = 2048,
            ContentType = "application/pdf",
            StorageUri = "/files/orders/abc.pdf",
            Description = "Signed copy",
            UploadedBy = "user-1",
            UploadedAt = uploadedAt,
        };

        attachment.ParentEntityId.Should().Be(parentId);
        attachment.FileName.Should().Be("contract.pdf");
        attachment.FileSize.Should().Be(2048);
        attachment.ContentType.Should().Be("application/pdf");
        attachment.StorageUri.Should().Be("/files/orders/abc.pdf");
        attachment.Description.Should().Be("Signed copy");
        attachment.UploadedBy.Should().Be("user-1");
        attachment.UploadedAt.Should().Be(uploadedAt);
    }

    [Fact]
    public void SupportsNonGuidParentKey()
    {
        var attachment = new IntKeyedAttachment { ParentEntityId = 42 };

        attachment.ParentEntityId.Should().Be(42);
    }

    [Fact]
    public void Type_IsAbstract()
        => typeof(AttachmentBase<Guid>).IsAbstract.Should().BeTrue();

    [Fact]
    public void Type_ImplementsIEntityOfGuid()
        => typeof(IEntity).IsAssignableFrom(typeof(AttachmentBase<Guid>)).Should().BeTrue();

    [Fact]
    public void Type_ImplementsISoftDelete()
        => typeof(ISoftDelete).IsAssignableFrom(typeof(AttachmentBase<Guid>)).Should().BeTrue();

    private sealed class TestAttachment : AttachmentBase<Guid>;

    private sealed class IntKeyedAttachment : AttachmentBase<int>;
}
