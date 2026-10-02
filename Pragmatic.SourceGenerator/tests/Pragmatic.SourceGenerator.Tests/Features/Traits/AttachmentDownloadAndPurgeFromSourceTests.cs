using System;
using System.Collections.Generic;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     The download endpoint and the retention job, verified through the REAL pipeline
///     (attribute → transform → model → template → compilation) rather than a hand-built model.
/// </summary>
/// <remarks>
///     Both features cross feature boundaries — the download endpoint is rendered by the shared
///     Endpoints templates and the purge job is injected into the Jobs pipeline — and a hand-built
///     model cannot show that the wiring holds. The purge job in particular is invisible to
///     <c>ForAttributeWithMetadataName</c> (a generator never sees its own output), so only a run from
///     source proves it gets an invoker, a DI registration and a recurring definition.
/// </remarks>
public class AttachmentDownloadAndPurgeFromSourceTests
{
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

    private static string Invoice(string options = "MaxPerEntity = 5")
        => InvoiceEntity.Replace("__OPTIONS__", options);

    private static string Concat(IReadOnlyDictionary<string, string> sources, string hintFragment)
        => string.Join("\n", sources.Where(s => s.Key.Contains(hintFragment)).Select(s => s.Value));

    // ── Download ─────────────────────────────────────────────────────────

    [Fact]
    public void Download_IsScopedToTheParent_NotOnlyToTheAttachmentId()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Invoice());

        var action = Concat(sources, "DownloadInvoiceAttachmentAction.Action");

        // THE anti-IDOR assertion: matching e.Id alone would let a caller who may read the
        // attachments of one invoice download the attachments of any other invoice.
        action.Should().Contain("e.Id == AttachmentId && e.InvoiceId == InvoiceId");
        action.Should().Contain("!e.IsDeleted", "a soft-deleted attachment must not be downloadable");
    }

    [Fact]
    public void Download_ServesTheBytesUnderItsOwnRoute_WithTheReadPermission()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Invoice());

        var endpoint = Concat(sources, "DownloadInvoiceAttachmentAction.Endpoint");

        endpoint.Should().Contain("MapGet(\"/api/billing/invoices/{invoiceId}/attachments/{attachmentId}/content\"");
        endpoint.Should().Contain("billing.invoice.attachments.read");

        // The success value is written as a file. Results.Ok(success) would serialize the
        // FileResponse record — Stream included — as JSON.
        endpoint.Should().Contain("FileResponseExtensions.ToResult(success, httpContext)");
        endpoint.Should().NotContain("Results.Ok(success)");
    }

    [Fact]
    public void Download_MissingBlob_Is404_NotAServerError()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Invoice());

        var action = Concat(sources, "DownloadInvoiceAttachmentAction.Action");

        action.Should().Contain("_storage.GetAsync(");
        // NotFoundError maps to 404. The blob having vanished out of band is an expected state of
        // the resource, not a fault of the server that a retry could fix.
        action.Should().Contain("if (content is null)");
        action.Should().Contain("NotFoundError.For(\"InvoiceAttachment\"");
    }

    [Fact]
    public void MetadataDto_DoesNotLeakTheStorageUri()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Invoice());

        var dto = sources.Should().ContainKey("Billing.Invoices.InvoiceAttachmentDto.Dto.g.cs").WhoseValue;

        dto.Should().NotContain("StorageUri",
            "the storage URI reveals the provider and the bucket layout, and on some providers is a "
            + "directly reachable URL; the content is served by the download endpoint instead");
        dto.Should().Contain("public string FileName", "the rest of the metadata stays");
        dto.Should().Contain("public string ContentType");
    }

    // ── Purge job ────────────────────────────────────────────────────────

    [Fact]
    public void PurgeJob_IsAbsentWithTheDefaultOptions()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Invoice());

        sources.Keys.Should().NotContain(k => k.Contains("PurgeInvoiceAttachmentsJob"),
            "an existing consumer must not suddenly get a background job that deletes its data");

        string.Join("\n", sources.Values).Should().NotContain("PurgeInvoiceAttachmentsJob",
            "no registration, invoker or recurring definition may reference a job that is not generated");
    }

    [Fact]
    public void PurgeJob_WithRetentionWindow_IsGeneratedAndFullyRegistered()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Invoice("PurgeDeletedAfterDays = 30"));

        var job = sources.Should().ContainKey("Billing.Invoices.PurgeInvoiceAttachmentsJob.Job.g.cs").WhoseValue;
        job.Should().Contain("[RecurringJob(\"0 3 * * *\", Id = \"purge-invoice-attachments\")]");
        job.Should().Contain("public sealed partial class PurgeInvoiceAttachmentsJob : IJob");

        // A generator cannot discover the [RecurringJob] on a class it emits itself, so without the
        // JobModel injection all three of these would be missing and the job would never run.
        sources.Keys.Should().Contain(k => k.Contains("PurgeInvoiceAttachmentsJob.Invoker"));
        Concat(sources, "_Infra.Jobs.Registration")
            .Should().Contain("TryAddScoped<global::Billing.Invoices.PurgeInvoiceAttachmentsJob>()");
        Concat(sources, "_Infra.Jobs.RecurringJobs")
            .Should().Contain("Id = \"purge-invoice-attachments\"");
        Concat(sources, "_Infra.Jobs.TypeRegistry")
            .Should().Contain("Billing.Invoices.PurgeInvoiceAttachmentsJob");
    }

    [Fact]
    public void PurgeJob_DeletesTheBlobBeforeTheRow_AndSurvivesOneFailure()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Invoice("PurgeDeletedAfterDays = 7"));

        var job = sources.Single(s => s.Key.Contains("PurgeInvoiceAttachmentsJob.Job")).Value;

        job.Should().Contain("AddDays(-7)");
        // The soft-delete query filter hides exactly the rows the job is looking for.
        job.Should().Contain(".IgnoreQueryFilters()");
        job.Should().Contain("a.IsDeleted && a.DeletedAt != null && a.DeletedAt < cutoff");

        // Blob first, row second: the row is the only pointer to the file.
        var storageDelete = job.IndexOf("_storage.DeleteAsync(", StringComparison.Ordinal);
        var rowRemove = job.IndexOf("_db.Remove(attachment)", StringComparison.Ordinal);
        storageDelete.Should().BeGreaterThan(0);
        rowRemove.Should().BeGreaterThan(storageDelete,
            "removing the row first and then failing leaks a blob nobody can find again");

        // One unreachable blob must not abort the run — the row is kept and retried.
        job.Should().Contain("catch (Exception ex)");
        job.Should().Contain("continue;");
        job.Should().Contain("catch (OperationCanceledException)", "cancellation is not a per-item failure");
    }

    [Fact]
    public void PurgeJob_CronIsOverridable()
    {
        var (sources, _) = TraitCompilationHarness.Generate(
            Invoice("PurgeDeletedAfterDays = 90, PurgeCron = \"30 2 * * 1\""));

        Concat(sources, "PurgeInvoiceAttachmentsJob")
            .Should().Contain("[RecurringJob(\"30 2 * * 1\"");
    }

    // ── Compilation ──────────────────────────────────────────────────────

    [Fact]
    public void DownloadAndPurge_CompileEndToEnd()
    {
        var source = Invoice("MaxPerEntity = 5, PurgeDeletedAfterDays = 30");

        var (traitErrors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            source,
            path =>
            {
                var f = System.IO.Path.GetFileName(path);
                return f.Contains("DownloadInvoiceAttachmentAction", StringComparison.Ordinal)
                       || f.Contains("PurgeInvoiceAttachmentsJob", StringComparison.Ordinal);
            });

        traitErrors.Should().BeEmpty(
            "the generated download action/endpoint and the purge job must compile. Errors:"
            + Environment.NewLine + TraitCompilationHarness.FormatErrors(traitErrors));
    }
}
