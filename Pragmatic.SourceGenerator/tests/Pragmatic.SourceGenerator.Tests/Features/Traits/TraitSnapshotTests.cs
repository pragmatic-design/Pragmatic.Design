using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
/// Verify snapshot tests for [HasComments] trait templates.
/// </summary>
public class TraitSnapshotTests
{
    private static CommentTraitModel BuildModel(
        bool allowReplies = true,
        bool requireApproval = false,
        bool supportInternalNotes = false,
        int maxLength = 2000) => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
        MaxLength = maxLength,
        AllowReplies = allowReplies,
        RequireApproval = requireApproval,
        SupportInternalNotes = supportInternalNotes,
    };

    [Fact]
    public Task CommentEntity_Default_MatchesSnapshot()
    {
        var source = new CommentEntityTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentEntity_NoReplies_MatchesSnapshot()
    {
        var source = new CommentEntityTemplate(BuildModel(allowReplies: false)).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentEntityConfig_Default_MatchesSnapshot()
    {
        var source = new CommentEntityConfigTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentEntityConfig_RequireApproval_MatchesSnapshot()
    {
        var source = new CommentEntityConfigTemplate(BuildModel(requireApproval: true)).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task ParentNavigation_Default_MatchesSnapshot()
    {
        var source = new ParentTraitNavigationTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentAddAction_Default_MatchesSnapshot()
    {
        var source = new CommentActionsTemplate(BuildModel(), CommentActionKind.Add).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentDeleteAction_Default_MatchesSnapshot()
    {
        var source = new CommentActionsTemplate(BuildModel(), CommentActionKind.Delete).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentPermissions_Default_MatchesSnapshot()
    {
        var source = new CommentPermissionsTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentPermissions_RequireApproval_MatchesSnapshot()
    {
        var source = new CommentPermissionsTemplate(BuildModel(requireApproval: true)).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentGetByIdAction_Default_MatchesSnapshot()
    {
        var source = new CommentActionsTemplate(BuildModel(), CommentActionKind.GetById).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentUpdateAction_Default_MatchesSnapshot()
    {
        var source = new CommentActionsTemplate(BuildModel(), CommentActionKind.Update).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentUpdateAction_WithEditWindow_MatchesSnapshot()
    {
        var model = BuildModel() with { EditWindowMinutes = 30 };
        var source = new CommentActionsTemplate(model, CommentActionKind.Update).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentModerateAction_MatchesSnapshot()
    {
        var source = new CommentActionsTemplate(BuildModel(requireApproval: true), CommentActionKind.Moderate).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentDto_Default_MatchesSnapshot()
    {
        var source = new CommentDtoTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }

    [Fact]
    public Task CommentListQuery_Default_MatchesSnapshot()
    {
        var source = new CommentListQueryTemplate(BuildModel()).RenderOutput().Text;
        return Verify(source);
    }
}
