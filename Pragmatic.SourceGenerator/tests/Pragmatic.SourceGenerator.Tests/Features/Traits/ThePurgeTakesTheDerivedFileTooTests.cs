using System;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     The retention job reclaims both files, not only the one the row was named after.
/// </summary>
/// <remarks>
///     <para>
///         The purge deletes the blob at <c>StorageUri</c> and then removes the row. A thumbnail is a
///         second file with no row of its own, so once the row is gone nothing in the system can name
///         it: it is unreachable by every query, invisible to the job that would have deleted it, and
///         permanent. That is the exact condition the purge job exists to prevent, one file to the
///         left.
///     </para>
///     <para>
///         ⚠️ The upload path already gets this rule right — its failure branches delete the derived
///         file before the original. The purge is the same rule at a different moment, and was not
///         updated with it.
///     </para>
/// </remarks>
public class ThePurgeTakesTheDerivedFileTooTests
{
    private static AttachmentTraitModel Model(int purgeAfterDays = 30) => new()
    {
        PurgeDeletedAfterDays = purgeAfterDays,
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId"
    };

    private static string Job(int purgeAfterDays = 30)
        => new AttachmentPurgeJobTemplate(Model(purgeAfterDays)).RenderOutput().Text;

    /// <summary>The setpoint: the derived file goes first, then the original, then the row.</summary>
    /// <remarks>
    ///     The order is the whole point. The row is the only thing that names either file, so removing
    ///     it before both are gone loses the address of whatever is left.
    /// </remarks>
    [Fact]
    public void ThePurge_DeletesTheThumbnailBeforeTheOriginalAndBoth_BeforeTheRow()
    {
        var source = Job();

        var thumbnail = source.IndexOf("attachment.ThumbnailUri", StringComparison.Ordinal);
        var original = source.IndexOf("attachment.StorageUri", StringComparison.Ordinal);
        var row = source.IndexOf("_db.Remove(attachment)", StringComparison.Ordinal);

        thumbnail.Should().BeGreaterThan(-1, "the derived file is reclaimed too, or it is permanent");
        original.Should().BeGreaterThan(thumbnail, "the original is the record; the derivation goes first");
        row.Should().BeGreaterThan(original, "and the row, which names both, goes last");
    }

    /// <summary>
    ///     ⚠️ The first control: an attachment with no thumbnail issues no second delete.
    /// </summary>
    /// <remarks>
    ///     Null is the ordinary case — every attachment that is not an image, and everything uploaded
    ///     before a thumbnail was ever declared. Without the guard the job would ask the provider to
    ///     delete an empty URI on every row, which is a throw on the providers that validate one and a
    ///     wasted round trip on the rest.
    /// </remarks>
    [Fact]
    public void ThePurge_AsksForNothingWhenThereIsNoThumbnail()
    {
        var source = Job();

        source.Should().Contain("if (attachment.ThumbnailUri is not null)",
            "a null ThumbnailUri is not an address to delete");
    }

    /// <summary>
    ///     ⚠️ The second control: a thumbnail that cannot be deleted leaves the row and does not fail
    ///     the run.
    /// </summary>
    /// <remarks>
    ///     The job already promises this for the original — one unreachable blob is retried next run
    ///     and never aborts the rest — and the derived file is treated the same rather than better:
    ///     inside the same <c>try</c>, so the same <c>catch</c> keeps the row and moves on. Deleting
    ///     the original after a failed thumbnail delete and then removing the row would produce
    ///     precisely the orphan this fixes.
    /// </remarks>
    [Fact]
    public void AThumbnailThatCannotBeDeleted_KeepsTheRowAndTheRunGoesOn()
    {
        var source = Job();

        var tryBlock = source.IndexOf("try", StringComparison.Ordinal);
        var thumbnail = source.IndexOf("attachment.ThumbnailUri", StringComparison.Ordinal);
        var catchBlock = source.IndexOf("catch (Exception ex)", StringComparison.Ordinal);

        thumbnail.Should().BeGreaterThan(tryBlock);
        thumbnail.Should().BeLessThan(catchBlock,
            "the same catch that keeps the row for the original keeps it for the derived file");

        source.Should().Contain("continue;",
            "one unreachable file does not abort the purge of every other attachment");
    }

    /// <summary>⚠️ And the control on all three: no retention configured, no job at all.</summary>
    /// <remarks>
    ///     The default is 0, which generates nothing — an existing consumer must never acquire a
    ///     background job that starts deleting its data because a derived file was added.
    /// </remarks>
    [Fact]
    public void WithoutRetention_NothingIsGenerated()
    {
        new AttachmentPurgeJobTemplate(Model(purgeAfterDays: 0)).RenderOutput().IsEmpty
            .Should().BeTrue();
    }
}
