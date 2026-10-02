using System;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     An uploaded image gets a thumbnail, derived where the file is stored.
/// </summary>
/// <remarks>
///     <para>
///         <c>[HasAttachments]</c> generated the whole upload path and offered no way to derive
///         anything from what was uploaded — no hook, no event, no field for a derived file. So a
///         four-megabyte screenshot was served whole to everybody who opened the story, and an
///         application could not fix it: a second upload action is a second door onto one resource,
///         and anything watching the database computes outside the upload's own transaction, where a
///         failed save would leave the derived file behind.
///     </para>
///     <para>
///         ⚠️ <b>The decision is made at compile time, not at runtime.</b> The derivation is emitted
///         only when the compilation references <c>Pragmatic.Imaging</c>. Not an
///         <c>IThumbnailer?</c> injected and null-checked: a capability that is always null is
///         indistinguishable from one that works, and generated code that says "maybe" is the defect
///         this repository keeps finding.
///     </para>
/// </remarks>
public class AThumbnailIsDerivedWhereTheFileIsStoredTests
{
    private static AttachmentTraitModel Model(int width = 0, int height = 0) => new()
    {
        ThumbnailMaxWidth = width,
        ThumbnailMaxHeight = height,
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId"
    };

    private static string Upload(int width = 0, int height = 0, bool imaging = true)
        => new AttachmentActionsTemplate(Model(width, height), AttachmentActionKind.Upload, imaging)
            .RenderOutput().Text;

    /// <summary>The setpoint: declared dimensions and a reference produce the derivation.</summary>
    [Fact]
    public void WithDimensionsAndImagingReferenced_TheUploadDerivesAndStoresAThumbnail()
    {
        var source = Upload(width: 240, height: 240);

        source.Should().Contain("global::Pragmatic.Imaging.ImagePipeline.Load(",
            "the bytes are decoded where they are stored, not on the read path — a resize on read is "
            + "a resize per viewer");
        source.Should().Contain("Thumbnail(240, 240)", "the declared bounds reach the call");
        source.Should().Contain("ThumbnailUri = ", "and what was derived is recorded on the row");
    }

    /// <summary>
    ///     ⚠️ The first control: no dimensions, no derivation — and no mention of the library.
    /// </summary>
    /// <remarks>
    ///     The default has to stay what it was. Every existing consumer of <c>[HasAttachments]</c>
    ///     declares no thumbnail, and emitting a call into a package they do not reference would not
    ///     compile.
    /// </remarks>
    [Fact]
    public void WithoutDimensions_TheUploadIsUnchanged()
    {
        var source = Upload();

        source.Should().NotContain("Pragmatic.Imaging");
        source.Should().NotContain("ThumbnailUri");
    }

    /// <summary>
    ///     ⚠️ The second control, and the compile-time decision: dimensions declared, library absent.
    /// </summary>
    /// <remarks>
    ///     Emitting the call anyway would fail the consumer's build inside generated code, which says
    ///     nothing about the attribute that caused it. Emitting a runtime probe instead would be the
    ///     "maybe" this repository forbids. So it emits nothing — and the generator reports it, which
    ///     is what keeps "nothing" from being silent.
    /// </remarks>
    [Fact]
    public void WithDimensionsButNoImagingReference_NothingIsEmitted()
    {
        var source = Upload(width: 240, height: 240, imaging: false);

        source.Should().NotContain("Pragmatic.Imaging");
        source.Should().NotContain("ThumbnailUri");
    }

    /// <summary>
    ///     ⚠️ A thumbnail must not outlive the upload that produced it.
    /// </summary>
    /// <remarks>
    ///     The original is already written when the row is saved, and the generated action deletes it
    ///     if that save fails — otherwise the bytes sit in storage with nothing pointing at them,
    ///     invisible to the purge job, which only ever looks at soft-deleted rows. A derived file has
    ///     exactly the same problem and no row of its own at all.
    /// </remarks>
    [Fact]
    public void WhenAThumbnailWasWritten_TheFailurePathDeletesItToo()
    {
        var source = Upload(width: 240, height: 240);

        source.Should().Contain("if (thumbnailUri is not null)",
            "the delete is conditional: a non-image never wrote one");
        source.Should().Contain("await _storage.DeleteAsync(thumbnailUri, ct)");
    }

    /// <summary>
    ///     ⚠️ A file that claims to be an image and does not decode is refused, not stored.
    /// </summary>
    /// <remarks>
    ///     A file that cannot be decoded at write time will not decode later either, and the
    ///     difference between the two moments is whether the failure has a caller to tell.
    /// </remarks>
    [Fact]
    public void AnImageThatDoesNotDecode_IsRefusedAtUpload()
    {
        var source = Upload(width: 240, height: 240);

        source.Should().Contain("catch (global::Pragmatic.Imaging.ImagingException",
            "the refusal is the library's own exception, not a bare catch");
        source.Should().Contain("BadRequestError",
            "and it reaches the caller as a 400 rather than as a 500");
        source.Should().Contain("await _storage.DeleteAsync(storageUri, ct)",
            "the original was already written when the decode failed, so the refusal takes it back out");
    }

    /// <summary>
    ///     ⚠️ And what is not an image is left alone, silently and on purpose.
    /// </summary>
    /// <remarks>
    ///     This is the case that turns "images get thumbnails" into "everything goes through an image
    ///     library". A PDF or a CSV must not reach the decoder at all — which is why the claim is read
    ///     from the extension, and the bytes only decide whether the claim was true.
    /// </remarks>
    [Fact]
    public void AFileThatDoesNotClaimToBeAnImage_NeverReachesTheDecoder()
    {
        var source = Upload(width: 240, height: 240);

        source.Should().Contain("global::Pragmatic.Imaging.ImageFormats.IsKnownExtension(ext)",
            "the list of decodable extensions belongs to the library, not to a copy inside the "
            + "generator that ages in silence");
    }

    // ── Through the real pipeline ────────────────────────────────────────
    //
    // The two above assert what the template renders; these assert what the generator decides. The
    // harness compiles WITHOUT Pragmatic.Imaging, which is exactly the case worth measuring: silence
    // and a warning, rather than silence alone.

    private const string InvoiceEntity = """
        using Pragmatic.Attachments;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace Billing.Invoices
        {
            public sealed class BillingBoundary { }

            [Entity]
            [Pragmatic.Persistence.Entity.BelongsTo<BillingBoundary>]
            [Resource("invoices")]
            [HasAttachments(__OPTIONS__)]
            public partial class Invoice : IEntity
            {
                public System.Guid Id { get; set; }
                public System.Guid PersistenceId { get => Id; set => Id = value; }
                public string Number { get; set; } = string.Empty;
            }

            [PragmaticDbContext("Billing")]
            public partial class BillingDbContext { }
        }
        """;

    private static string Invoice(string options) => InvoiceEntity.Replace("__OPTIONS__", options);

    /// <summary>
    ///     ⚠️ Asked for and unreachable is reported, not silently dropped.
    /// </summary>
    /// <remarks>
    ///     This is the outcome the compile-time decision creates and has to answer for: an attribute
    ///     that reads as configured while every upload keeps storing the original whole. Without the
    ///     warning it is invisible — the build is clean and nothing is generated.
    /// </remarks>
    [Fact]
    public void AThumbnailAskedForWithoutTheLibrary_IsReported()
    {
        var (sources, diagnostics) = TraitCompilationHarness.Generate(
            Invoice("ThumbnailMaxWidth = 240, ThumbnailMaxHeight = 240"));

        diagnostics.Should().Contain(d => d.Id == "PRAG2651",
            "the harness compiles without Pragmatic.Imaging, which is the case a consumer hits first");

        var upload = string.Join("\n",
            sources.Where(s => s.Key.Contains("UploadInvoiceAttachmentAction")).Select(s => s.Value));

        upload.Should().NotContain("Pragmatic.Imaging",
            "and nothing is emitted, so the consumer's build does not fail inside generated code");
    }

    /// <summary>
    ///     ⚠️ The action the template writes also has to reach the Actions pipeline.
    /// </summary>
    /// <remarks>
    ///     Written after an end-to-end run found it, and that is the honest order: the class was
    ///     generated, the route was mapped, and the action model was not — so the endpoint resolved
    ///     <c>IDomainActionInvoker&lt;DownloadThumbnail…, FileResponse&gt;</c>, found nothing, and
    ///     answered 500. Two halves have to be emitted together, and nothing in the generator suite
    ///     compared them.
    /// </remarks>
    [Fact]
    public void TheThumbnailAction_GetsAnInvokerAndItsStorage()
    {
        var actions = TraitActionModelBuilder.BuildAttachmentActions(
            Model(240, 240), includeDownload: true, includeThumbnail: true);

        var thumbnail = actions.SingleOrDefault(a => a.TypeName.StartsWith("DownloadThumbnail", StringComparison.Ordinal));

        thumbnail.Should().NotBeNull("without a model there is no invoker and no DI registration");
        thumbnail!.Dependencies.Should().Contain(d => d.FieldName == "_storage",
            "the field the template declares must be one this list assigns, or it stays null");
    }

    /// <summary>⚠️ And not when no thumbnail is derived.</summary>
    [Fact]
    public void WithoutAThumbnail_NoSuchActionIsRegistered()
    {
        var actions = TraitActionModelBuilder.BuildAttachmentActions(Model(), includeDownload: true);

        actions.Should().NotContain(a => a.TypeName.StartsWith("DownloadThumbnail", StringComparison.Ordinal));
    }

    /// <summary>⚠️ The control: no thumbnail asked for, nothing said.</summary>
    /// <remarks>
    ///     A warning that fires for every consumer of <c>[HasAttachments]</c> is a warning everybody
    ///     learns to ignore, and the default asks for no thumbnail at all.
    /// </remarks>
    [Fact]
    public void AnAttachmentThatAsksForNoThumbnail_IsNotReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Invoice("MaxPerEntity = 5"));

        diagnostics.Should().NotContain(d => d.Id == "PRAG2651");
    }
}
