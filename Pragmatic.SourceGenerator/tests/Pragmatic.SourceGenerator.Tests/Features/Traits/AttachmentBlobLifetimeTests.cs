using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     Documented contract of <c>[HasAttachments]</c> (see the attribute's XML doc and
///     <c>docs/concepts.md#retention-and-purging</c>): deleting an attachment is a <b>soft</b> delete and
///     deliberately leaves the file in <c>IFileStorage</c>, because the operation is reversible.
///     Reclaiming the blob is a separate, scheduled decision — <c>PurgeDeletedAfterDays</c> — and the
///     delete action must keep its hands off storage either way.
/// </summary>
public class AttachmentBlobLifetimeTests
{
    private static AttachmentTraitModel Model(int purgeDeletedAfterDays = 0) => new()
    {
        PurgeDeletedAfterDays = purgeDeletedAfterDays,
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId"
    };

    private static string Render(AttachmentActionKind kind, int purgeDeletedAfterDays = 0)
        => new AttachmentActionsTemplate(Model(purgeDeletedAfterDays), kind).RenderOutput().Text;

    [Fact]
    public void DeleteAction_SoftDeletesTheRowAndNeverTouchesStorage()
    {
        var source = Render(AttachmentActionKind.Delete);

        source.Should().Contain("attachment.IsDeleted = true;");
        source.Should().Contain("attachment.DeletedAt = _clock.UtcNow;");
        source.Should().Contain("attachment.DeletedBy = _currentUser.Id;");

        // No storage call, and no IFileStorage dependency to make one with: the blob outlives the row
        // on purpose, so a restore is still possible. (The word IFileStorage does appear — in the
        // comment that says the file is deliberately kept.)
        source.Should().NotContain("private IFileStorage");
        source.Should().NotContain("_storage");
        source.Should().NotContain("StorageUri");
    }

    /// <summary>
    ///     The comment must describe the retention that is actually configured — pointing at a purge
    ///     job that was never generated is exactly the kind of claim this test exists to prevent.
    /// </summary>
    [Fact]
    public void DeleteAction_WithoutPurgeOption_SaysCleanupIsTheApplicationsJob()
    {
        var source = Render(AttachmentActionKind.Delete);

        source.Should().Contain("intentionally NOT removed from IFileStorage");
        source.Should().Contain("responsibility");
        source.Should().Contain("PurgeDeletedAfterDays");
        source.Should().NotContain("PurgeReservationAttachmentsJob",
            "no job is generated at the default, so the code must not name one");
    }

    [Fact]
    public void DeleteAction_WithPurgeOption_PointsAtTheGeneratedJob()
    {
        var source = Render(AttachmentActionKind.Delete, purgeDeletedAfterDays: 30);

        source.Should().Contain("intentionally NOT removed from IFileStorage");
        source.Should().Contain("PurgeReservationAttachmentsJob");
        source.Should().Contain("PurgeDeletedAfterDays = 30");
    }

    [Fact]
    public void DeleteAction_NeverTouchesStorage_EvenWhenPurgeIsEnabled()
    {
        var source = Render(AttachmentActionKind.Delete, purgeDeletedAfterDays: 30);

        source.Should().NotContain("private IFileStorage");
        source.Should().NotContain("_storage.");
        source.Should().NotContain("StorageUri");
    }

    [Fact]
    public void OnlyUploadWritesToStorage_AndOnlyDownloadReadsFromIt()
    {
        // Upload is the single generated action that WRITES to storage and download the single one
        // that reads from it; metadata-read and delete only ever see the DbContext. That is what
        // keeps "a soft delete cannot destroy the file" a property of the generated surface.
        Render(AttachmentActionKind.Upload).Should().Contain("private IFileStorage _storage = null!;");
        Render(AttachmentActionKind.Upload).Should().Contain("_storage.SaveAsync(");

        Render(AttachmentActionKind.Download).Should().Contain("private IFileStorage _storage = null!;");
        Render(AttachmentActionKind.Download).Should().Contain("_storage.GetAsync(");
        Render(AttachmentActionKind.Download).Should().NotContain("_storage.DeleteAsync(");

        Render(AttachmentActionKind.GetById).Should().NotContain("_storage");
        Render(AttachmentActionKind.Delete).Should().NotContain("_storage");
    }
}
