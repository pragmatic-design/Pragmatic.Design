using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
/// Verify snapshot tests for the [HasTags] read-side templates (DTO + list query).
/// </summary>
public class TagTraitSnapshotTests
{
    private static TagTraitModel BuildModel() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
    };

    [Fact]
    public Task TagDto_Default_MatchesSnapshot()
    {
        var source = new TagDtoTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task TagListQuery_Default_MatchesSnapshot()
    {
        var source = new TagListQueryTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }
}
