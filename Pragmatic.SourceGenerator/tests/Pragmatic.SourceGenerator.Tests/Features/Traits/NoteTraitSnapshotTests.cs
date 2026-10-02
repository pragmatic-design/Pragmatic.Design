using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     Verify snapshots for the <c>[HasNotes&lt;T&gt;]</c> templates. Notes was the one trait with no
///     snapshot at all: its entity, config, DTO, query, permissions and four actions were covered only
///     by a single "it compiles" test, so a change in what they emit had nothing to fail against.
/// </summary>
public class NoteTraitSnapshotTests
{
    private static NoteTraitModel BuildModel(
        bool allowEditing = true,
        int editWindowMinutes = -1,
        int maxLength = 4000) => new()
    {
        ParentTypeName = "Guest",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Guest",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "guests",
        ResourceParamName = "guestId",
        MaxLength = maxLength,
        AllowEditing = allowEditing,
        EditWindowMinutes = editWindowMinutes,
    };

    [Fact]
    public Task NoteEntity_Default_MatchesSnapshot()
        => Verify(new NoteEntityTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task NoteEntityConfig_Default_MatchesSnapshot()
        => Verify(new NoteEntityConfigTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task NoteEntityConfig_CustomMaxLength_MatchesSnapshot()
        => Verify(new NoteEntityConfigTemplate(BuildModel(maxLength: 500)).RenderOutput().Text);

    [Fact]
    public Task NoteAddAction_Default_MatchesSnapshot()
        => Verify(new NoteActionsTemplate(BuildModel(), NoteActionKind.Add).RenderOutput().Text);

    [Fact]
    public Task NoteGetByIdAction_Default_MatchesSnapshot()
        => Verify(new NoteActionsTemplate(BuildModel(), NoteActionKind.GetById).RenderOutput().Text);

    [Fact]
    public Task NoteUpdateAction_Default_MatchesSnapshot()
        => Verify(new NoteActionsTemplate(BuildModel(), NoteActionKind.Update).RenderOutput().Text);

    [Fact]
    public Task NoteUpdateAction_WithEditWindow_MatchesSnapshot()
        => Verify(new NoteActionsTemplate(BuildModel(editWindowMinutes: 30), NoteActionKind.Update)
            .RenderOutput().Text);

    [Fact]
    public Task NoteDeleteAction_Default_MatchesSnapshot()
        => Verify(new NoteActionsTemplate(BuildModel(), NoteActionKind.Delete).RenderOutput().Text);

    [Fact]
    public Task NoteDto_Default_MatchesSnapshot()
        => Verify(new NoteDtoTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task NoteListQuery_Default_MatchesSnapshot()
        => Verify(new NoteListQueryTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task NotePermissions_Default_MatchesSnapshot()
        => Verify(new NotePermissionsTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task NotePermissions_NoEditing_MatchesSnapshot()
        => Verify(new NotePermissionsTemplate(BuildModel(allowEditing: false)).RenderOutput().Text);

    [Fact]
    public Task ParentNoteNavigation_Default_MatchesSnapshot()
        => Verify(new ParentNoteNavigationTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public void BuildNoteEndpoints_EmitsFiveRoutes_EachGatedOnItsOwnPermission()
    {
        var endpoints = TraitEndpointModelBuilder.BuildNoteEndpoints(BuildModel());

        endpoints.Select(e => $"{e.HttpMethod} {e.Route}").Should().BeEquivalentTo(
            "Post /api/booking/guests/{guestId}/notes",
            "Get /api/booking/guests/{guestId}/notes",
            "Get /api/booking/guests/{guestId}/notes/{noteId}",
            "Put /api/booking/guests/{guestId}/notes/{noteId}",
            "Delete /api/booking/guests/{guestId}/notes/{noteId}");

        endpoints.Should().OnlyContain(e => e.Authorization!.IsRequired);
        endpoints.SelectMany(e => e.Authorization!.RequiredPermissions.AsImmutableArray())
            .Should().OnlyContain(p => p.StartsWith("booking.guest.notes.", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildNoteEndpoints_WithoutEditing_OmitsThePutRoute()
    {
        var endpoints = TraitEndpointModelBuilder.BuildNoteEndpoints(BuildModel(allowEditing: false));

        endpoints.Should().NotContain(e => e.HttpMethod == "Put");
        endpoints.Should().HaveCount(4);
    }
}
