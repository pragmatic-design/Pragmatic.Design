using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     <c>[HasComments(SupportInternalNotes = true)]</c>: the permission that governs internal comments,
///     the row filter that hides them, and the two actions that do not pass through that filter. The
///     effect over HTTP is measured in the Showcase (<c>InternalCommentsTests</c>).
/// </summary>
public class InternalCommentsTemplateTests
{
    private const string ViewInternal = "booking.reservation.comments.view-internal";

    private static CommentTraitModel BuildModel(bool supportInternalNotes) => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
        MaxLength = 2000,
        SupportInternalNotes = supportInternalNotes,
    };

    [Fact]
    public void Permissions_WithInternalNotes_DeclareViewInternal()
    {
        var source = new CommentPermissionsTemplate(BuildModel(true)).RenderOutput().Text;

        source.Should().Contain($"public const string ViewInternal = \"{ViewInternal}\";");
    }

    /// <summary>A constant that gates nothing would advertise a grantable permission with no effect.</summary>
    [Fact]
    public void Permissions_WithoutInternalNotes_DeclareNoViewInternal()
    {
        var source = new CommentPermissionsTemplate(BuildModel(false)).RenderOutput().Text;

        source.Should().NotContain("ViewInternal");
    }

    [Fact]
    public void Filter_WithInternalNotes_HidesInternalRows_UnlessViewInternal()
    {
        var source = new CommentInternalVisibilityFilterTemplate(BuildModel(true)).RenderOutput().Text;

        source.Should().Contain("public sealed class InternalVisibilityFilter");
        source.Should().Contain($"BypassPermission => \"{ViewInternal}\"");
        source.Should().Contain("entity.Visibility != global::Pragmatic.Comments.CommentVisibility.Internal");
    }

    [Fact]
    public void Filter_WithoutInternalNotes_IsNotEmitted()
    {
        var artifact = new CommentInternalVisibilityFilterTemplate(BuildModel(false)).RenderOutput();

        artifact.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AddAction_WithInternalNotes_RefusesAnInternalComment_WithoutViewInternal()
    {
        var source = new CommentActionsTemplate(BuildModel(true), CommentActionKind.Add).RenderOutput().Text;

        source.Should().Contain("if (Visibility == CommentVisibility.Internal");
        source.Should().Contain($"HasPermissionAsync(\"{ViewInternal}\", ct)");
        source.Should().Contain($"ForbiddenError.MissingPermission(\"{ViewInternal}\")");
    }

    /// <summary>The get-by-id reads the DbSet, which the row filter does not reach.</summary>
    [Fact]
    public void GetByIdAction_WithInternalNotes_ChecksTheVisibilityItself()
    {
        var source = new CommentActionsTemplate(BuildModel(true), CommentActionKind.GetById).RenderOutput().Text;

        source.Should().Contain($"var canViewInternal = await _currentUser.Authorization.HasPermissionAsync(\"{ViewInternal}\", ct)");
        source.Should().Contain("&& (canViewInternal || e.Visibility != CommentVisibility.Internal)");
    }

    [Fact]
    public void GetByIdAction_WithoutInternalNotes_AsksForNoPermission()
    {
        var source = new CommentActionsTemplate(BuildModel(false), CommentActionKind.GetById).RenderOutput().Text;

        source.Should().NotContain("canViewInternal");
    }
}
