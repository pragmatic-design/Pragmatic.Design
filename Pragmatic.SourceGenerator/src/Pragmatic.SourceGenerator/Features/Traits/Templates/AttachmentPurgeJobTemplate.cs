using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Logging.Templates;
using Pragmatic.SourceGenerator.Features.Logging.Transforms;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     Generates <c>Purge{Parent}AttachmentsJob</c>, the <c>[RecurringJob]</c> that reclaims the blobs
///     of attachments soft-deleted longer ago than
///     <c>[HasAttachments(PurgeDeletedAfterDays = N)]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Emitted only when the option is set: the default (0) must leave an existing consumer exactly as
///         it was, with no background job that starts deleting its data.
///     </para>
///     <para>
///         Its two log lines are Pragmatic call sites written into the job in this pass: Microsoft's
///         generator would never see a <c>[LoggerMessage]</c> declared here, and the method would get no
///         body.
///     </para>
/// </remarks>
internal sealed class AttachmentPurgeJobTemplate : LogCallSiteTemplateBase
{
    private readonly AttachmentTraitModel _model;

    public AttachmentPurgeJobTemplate(AttachmentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";
    protected override string? TriggerInfo => $"[HasAttachments(PurgeDeletedAfterDays = {_model.PurgeDeletedAfterDays})] on {_model.ParentTypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.PurgeJobTypeName, "Job", _model.ParentNamespace), ToSourceText());

    protected override bool Validate() => _model.PurgeEnabled;

    public override void RenderFile()
    {
        AddUsing("System.Linq");
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.Extensions.Logging");
        AddUsing("Pragmatic.Jobs");
        AddUsing("Pragmatic.Jobs.Attributes");
        AddUsing("Pragmatic.Storage");
        AddUsing("Pragmatic.Temporal.Clock");
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        XmlSummary(
            $"SG-generated retention job for <see cref=\"{_model.AttachmentTypeName}\"/>: removes the stored " +
            $"file and then the metadata row of attachments soft-deleted more than {_model.PurgeDeletedAfterDays} day(s) ago.");
        AppendLine($"[RecurringJob(\"{StringHelper.CSharpLiteral(_model.EffectivePurgeCron)}\", Id = \"{PurgeJobId}\")]");

        Class(_model.PurgeJobTypeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, Sealed = true },
            interfaces: new List<string> { "IJob" });
    }

    /// <summary>
    ///     Matches what <c>JobTransform.DeriveJobId</c> derives for a hand-written job class, so the
    ///     recurring-definition id is the same string whether the class is generated or not.
    /// </summary>
    private string PurgeJobId
        => I18nNamingHelper.ToKebabCase($"Purge{_model.ParentTypeName}Attachments");

    private void RenderBody()
    {
        var logger = $"global::Microsoft.Extensions.Logging.ILogger<{_model.PurgeJobTypeName}>";

        AppendLine("private readonly DbContext _db;");
        AppendLine("private readonly IFileStorage _storage;");
        AppendLine("private readonly IClock _clock;");
        AppendLine($"private readonly {logger} _logger;");
        AppendLine();

        // The DbContext is resolved with the boundary key, exactly like the generated actions:
        // a module with several boundaries has one DbContext per boundary and the unkeyed
        // resolution would either fail or hand back somebody else's database.
        var dbParam = _model.BoundaryFullTypeName is not null
            ? $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof(global::{_model.BoundaryFullTypeName}))] DbContext db"
            : "DbContext db";

        AppendLine($"public {_model.PurgeJobTypeName}(");
        IncreaseIndent();
        AppendLine($"{dbParam},");
        AppendLine("IFileStorage storage,");
        AppendLine("IClock clock,");
        AppendLine($"{logger} logger)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("_db = db;");
            AppendLine("_storage = storage;");
            AppendLine("_clock = clock;");
            AppendLine("_logger = logger;");
        });
        AppendLine();

        XmlSummary(
            "Deletes the stored files first — the derived thumbnail, then the original — and the row "
            + "second, for every expired attachment.");
        AppendLine("public async Task ExecuteAsync(JobContext context, CancellationToken ct)");
        Block(RenderExecuteBody);
        AppendLine();
        RenderLogMethods();
    }

    private void RenderLogMethods()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var filesKept = GeneratedLogCallSite.Create(
            "LogFilesKept", "private", "_logger", "Warning",
            $"Purge of {_model.AttachmentTypeName} {{AttachmentId}} left its stored files in place; the row is kept and will be retried.",
            [("global::System.Guid", "attachmentId")],
            exception: "ex");
        RenderCallSite(filesKept, StateName(filesKept, names));
        AppendLine();

        var purged = GeneratedLogCallSite.Create(
            "LogPurged", "private", "_logger", "Information",
            $"Purged {{PurgedCount}} of {{ExpiredCount}} expired {_model.AttachmentTypeName} rows.",
            [("int", "purgedCount"), ("int", "expiredCount")]);
        RenderCallSite(purged, StateName(purged, names));
    }

    private void RenderExecuteBody()
    {
        AppendLine($"var cutoff = _clock.UtcNow.AddDays(-{_model.PurgeDeletedAfterDays});");
        AppendLine();
        // IgnoreQueryFilters is not an optimization: the generated EF configuration applies
        // HasQueryFilter(e => !e.IsDeleted), so without it the query returns nothing at all and the
        // job silently purges zero rows forever.
        AppendLine($"var expired = await _db.Set<{_model.AttachmentTypeName}>()");
        AppendLine("    .IgnoreQueryFilters()");
        AppendLine("    .Where(a => a.IsDeleted && a.DeletedAt != null && a.DeletedAt < cutoff)");
        AppendLine("    .ToListAsync(ct).ConfigureAwait(false);");
        AppendLine();
        AppendLine("if (expired.Count == 0)");
        IncreaseIndent();
        AppendLine("return;");
        DecreaseIndent();
        AppendLine();
        AppendLine("var purged = 0;");
        AppendLine("foreach (var attachment in expired)");
        Block(() =>
        {
            AppendLine("ct.ThrowIfCancellationRequested();");
            AppendLine();
            // Blob first, row second. The row is the only pointer to the file: removing it first and
            // then failing leaks a blob nobody can ever find again, while this order leaves the row
            // for the next run — the delete is idempotent, so the retry costs nothing.
            AppendLine("try");
            Block(() =>
            {
                // The derived file first, and inside the same try. It has no row of its own, so the
                // row being removed is what would make it unreachable: once that happens nothing in
                // the system can name it, no query returns it and this job will never see it again.
                //
                // ⚠️ Guarded on null because null is the ordinary case — every attachment that is not
                // an image, and everything uploaded before a thumbnail was declared. Handing an empty
                // URI to a provider is a throw on the ones that validate it.
                //
                // Treated exactly like the original rather than better: a failure here takes the same
                // catch, keeps the row and retries next run. Deleting the original after a failed
                // thumbnail delete and then removing the row would leave precisely the orphan this
                // whole block exists to prevent, and both deletes are idempotent, so the retry is free.
                AppendLine("if (attachment.ThumbnailUri is not null)");
                Block(() =>
                {
                    AppendLine("await _storage.DeleteAsync(new Uri(attachment.ThumbnailUri, UriKind.RelativeOrAbsolute), ct)");
                    AppendLine("    .ConfigureAwait(false);");
                });
                AppendLine();
                AppendLine("await _storage.DeleteAsync(new Uri(attachment.StorageUri, UriKind.RelativeOrAbsolute), ct)");
                AppendLine("    .ConfigureAwait(false);");
            });
            AppendLine("catch (OperationCanceledException)");
            Block(() => AppendLine("throw;"));
            // One unreachable blob must not abort the run: the row is left behind and retried on the
            // next schedule, while every other attachment is still purged.
            AppendLine("catch (Exception ex)");
            Block(() =>
            {
                AppendLine("LogFilesKept(attachment.Id, ex);");
                AppendLine("continue;");
            });
            AppendLine();
            AppendLine("_db.Remove(attachment);");
            AppendLine("purged++;");
        });
        AppendLine();
        AppendLine("if (purged > 0)");
        Block(() =>
        {
            // Retention is erasure: these rows are past the window that justified keeping them, and the
            // blob is already gone. Without saying so the soft-delete interceptor would turn the purge
            // into a second flagging of rows that are flagged already, and the table would grow forever
            // while the job reported success.
            AppendLine("using (global::Pragmatic.Persistence.Entity.SoftDeleteScope.Suspend())");
            Block(() => AppendLine("await _db.SaveChangesAsync(ct).ConfigureAwait(false);"));
            AppendLine("LogPurged(purged, expired.Count);");
        });
    }
}
