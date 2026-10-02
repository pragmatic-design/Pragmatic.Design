using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
/// Verify snapshot tests for [HasAttachments] trait templates, plus the action/endpoint models
/// that carry the attribute's limits into the generated pipeline.
/// </summary>
public class AttachmentTraitSnapshotTests
{
    private static AttachmentTraitModel BuildModel(
        int maxPerEntity = 20,
        long maxFileSizeBytes = 10_485_760,
        string allowedExtensions = "",
        string? container = null,
        int purgeDeletedAfterDays = 0,
        string purgeCron = "0 3 * * *") => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
        MaxPerEntity = maxPerEntity,
        MaxFileSizeBytes = maxFileSizeBytes,
        AllowedExtensions = allowedExtensions,
        ContainerOverride = container,
        PurgeDeletedAfterDays = purgeDeletedAfterDays,
        PurgeCron = purgeCron,
    };

    [Fact]
    public Task AttachmentEntity_Default_MatchesSnapshot()
        => Verify(new AttachmentEntityTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task AttachmentEntityConfig_Default_MatchesSnapshot()
        => Verify(new AttachmentEntityConfigTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task ParentAttachmentNavigation_Default_MatchesSnapshot()
        => Verify(new ParentAttachmentNavigationTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task AttachmentUploadAction_Default_MatchesSnapshot()
        => Verify(new AttachmentActionsTemplate(BuildModel(), AttachmentActionKind.Upload).RenderOutput().Text);

    [Fact]
    public Task AttachmentUploadAction_WithExtensionAllowList_MatchesSnapshot()
        => Verify(new AttachmentActionsTemplate(
            BuildModel(maxPerEntity: 5, allowedExtensions: ".pdf, PNG", container: "invoices"),
            AttachmentActionKind.Upload).RenderOutput().Text);

    [Fact]
    public Task AttachmentGetByIdAction_Default_MatchesSnapshot()
        => Verify(new AttachmentActionsTemplate(BuildModel(), AttachmentActionKind.GetById).RenderOutput().Text);

    [Fact]
    public Task AttachmentDownloadAction_Default_MatchesSnapshot()
        => Verify(new AttachmentActionsTemplate(BuildModel(), AttachmentActionKind.Download).RenderOutput().Text);

    [Fact]
    public Task AttachmentDeleteAction_Default_MatchesSnapshot()
        => Verify(new AttachmentActionsTemplate(BuildModel(), AttachmentActionKind.Delete).RenderOutput().Text);

    [Fact]
    public Task AttachmentPurgeJob_WithRetentionWindow_MatchesSnapshot()
        => Verify(new AttachmentPurgeJobTemplate(BuildModel(purgeDeletedAfterDays: 30))
            .RenderOutput().Text);

    [Fact]
    public Task AttachmentPermissions_Default_MatchesSnapshot()
        => Verify(new AttachmentPermissionsTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task AttachmentDto_Default_MatchesSnapshot()
        => Verify(new AttachmentDtoTemplate(BuildModel()).RenderOutput().Text);

    [Fact]
    public Task AttachmentListQuery_Default_MatchesSnapshot()
        => Verify(new AttachmentListQueryTemplate(BuildModel()).RenderOutput().Text);

    // ── Model builders ───────────────────────────────────────────────────

    /// <summary>
    ///     The attribute documents that the leading dot is optional in <c>AllowedExtensions</c>.
    ///     The check compares against <c>Path.GetExtension</c>, which always returns the dot, so
    ///     an un-normalized "png" would reject every upload it was meant to allow.
    /// </summary>
    [Fact]
    public void AttachmentUploadAction_NormalizesExtensionsWithoutLeadingDot()
    {
        var source = new AttachmentActionsTemplate(
            BuildModel(allowedExtensions: "pdf, .PNG , "),
            AttachmentActionKind.Upload).RenderOutput().Text;

        source.Should().Contain("""new[] { ".pdf", ".png" }""");
    }

    [Fact]
    public void BuildAttachmentActions_ProducesUploadGetDownloadDelete()
    {
        var actions = TraitActionModelBuilder.BuildAttachmentActions(BuildModel());

        actions.Select(a => a.TypeName).Should().BeEquivalentTo(
            "UploadReservationAttachmentAction",
            "GetReservationAttachmentAction",
            "DownloadReservationAttachmentAction",
            "DeleteReservationAttachmentAction");
    }

    /// <summary>
    ///     The download action's return type lives in Pragmatic.Endpoints; a module that references
    ///     Actions but not Endpoints must not get an action model for a class that is not generated.
    /// </summary>
    [Fact]
    public void BuildAttachmentActions_WithoutEndpoints_OmitsTheDownloadAction()
        => TraitActionModelBuilder.BuildAttachmentActions(BuildModel(), includeDownload: false)
            .Select(a => a.TypeName).Should().NotContain("DownloadReservationAttachmentAction");

    /// <summary>
    ///     The fields declared by the template and the dependencies declared on the model must match:
    ///     a field the model does not know about is never assigned by the generated SetDependencies.
    /// </summary>
    [Fact]
    public void BuildAttachmentActions_DownloadDeclaresTheStorageDependency()
    {
        var download = TraitActionModelBuilder.BuildAttachmentActions(BuildModel())
            .Single(a => a.TypeName.StartsWith("Download"));

        download.Dependencies.Select(d => d.FieldName).Should().BeEquivalentTo("_db", "_storage");
        download.ReturnTypeName.Should().Be("global::Pragmatic.Endpoints.Responses.FileResponse");
    }

    [Fact]
    public void BuildAttachmentEndpoints_CarriesTheUploadLimitsOntoTheEndpoint()
    {
        var endpoints = TraitEndpointModelBuilder.BuildAttachmentEndpoints(
            BuildModel(maxFileSizeBytes: 2048, allowedExtensions: ".pdf, PNG"));

        var upload = endpoints.Single(e => e.TypeName.StartsWith("Upload"));
        upload.Route.Should().Be("/api/booking/reservations/{reservationId}/attachments");
        upload.AttachmentUpload.Should().NotBeNull();
        upload.AttachmentUpload!.MaxFileSizeBytes.Should().Be(2048);

        // Leading dot optional, case-insensitive — the contract the attribute documents.
        upload.AttachmentUpload.AllowedExtensions.Should().BeEquivalentTo(".pdf", ".png");
    }

    [Fact]
    public void BuildAttachmentEndpoints_GatesEveryOperationOnItsOwnPermission()
    {
        var endpoints = TraitEndpointModelBuilder.BuildAttachmentEndpoints(BuildModel());

        endpoints.Should().OnlyContain(e => e.Authorization != null && e.Authorization.IsRequired);
        endpoints.SelectMany(e => e.Authorization!.RequiredPermissions).Should().BeEquivalentTo(
            "booking.reservation.attachments.upload",
            "booking.reservation.attachments.read",
            "booking.reservation.attachments.read",
            "booking.reservation.attachments.read",
            "booking.reservation.attachments.delete");
    }

    /// <summary>
    ///     The content lives under its own route, is gated on the SAME permission as the metadata it
    ///     describes, and carries both ids — the parent one is what stops an IDOR.
    /// </summary>
    [Fact]
    public void BuildAttachmentEndpoints_DownloadIsParentScopedAndUsesTheReadPermission()
    {
        var download = TraitEndpointModelBuilder.BuildAttachmentEndpoints(BuildModel())
            .Single(e => e.TypeName.StartsWith("Download"));

        download.HttpMethod.Should().Be("Get");
        download.Route.Should().Be(
            "/api/booking/reservations/{reservationId}/attachments/{attachmentId}/content");
        download.Authorization!.RequiredPermissions.Should()
            .BeEquivalentTo("booking.reservation.attachments.read");
        download.RouteParameters.Select(p => p.Name).Should()
            .BeEquivalentTo("reservationId", "attachmentId");

        // Drives the file branch of DomainActionHandlerTemplate instead of Results.Ok(success).
        download.IsFileResponse.Should().BeTrue();
    }

    // ── Purge job ────────────────────────────────────────────────────────

    [Fact]
    public void PurgeJob_IsNotGeneratedByDefault()
    {
        BuildModel().PurgeEnabled.Should().BeFalse("the default must not change existing behaviour");
        new AttachmentPurgeJobTemplate(BuildModel()).RenderOutput().Text!.Length.Should().Be(0);
        TraitJobModelBuilder.BuildAttachmentPurgeJob(BuildModel()).Should().BeNull();
    }

    [Fact]
    public void PurgeJob_WithRetentionWindow_IsInjectedIntoTheJobsPipeline()
    {
        var job = TraitJobModelBuilder.BuildAttachmentPurgeJob(
            BuildModel(purgeDeletedAfterDays: 14, purgeCron: "0 4 * * 0"));

        job.Should().NotBeNull();
        job!.IsRecurring.Should().BeTrue();
        job.CronExpression.Should().Be("0 4 * * 0", "the cron must be overridable");
        job.RecurringJobId.Should().Be("purge-reservation-attachments");
        job.TypeName.Should().Be("PurgeReservationAttachmentsJob");
        // Without these the Jobs pipeline reports PRAG2500/PRAG2502 against generated code.
        job.ImplementsJobInterface.Should().BeTrue();
        job.IsPartial.Should().BeTrue();
    }

    [Fact]
    public void BuildAttachmentEndpoints_WithoutResource_ProducesNothing()
        => TraitEndpointModelBuilder
            .BuildAttachmentEndpoints(BuildModel() with { ResourceSegment = null })
            .Should().BeEmpty();
}
