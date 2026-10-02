namespace Pragmatic.Attachments;

/// <summary>
///     Marks an entity as having file attachments (1:N).
///     SG generates the file metadata entity, upload/read/delete actions, and REST endpoints.
///     Files are stored via <see cref="Pragmatic.Storage.IFileStorage"/>.
///     <para>The SG generates per consumer entity:</para>
///     <list type="bullet">
///         <item><description>A typed child entity <c>{Parent}Attachment</c> with file metadata</description></item>
///         <item><description>EF Core configuration with FK to parent</description></item>
///         <item><description>Actions: Upload, GetById (metadata), Download (content), Delete</description></item>
///         <item><description>REST endpoints under <c>/api/.../{{parentId}}/attachments</c></description></item>
///         <item><description>Navigation property <c>ICollection&lt;{Parent}Attachment&gt; Attachments</c></description></item>
///     </list>
///     <para>
///     The read side is split in two: <c>GET .../attachments/{{attachmentId}}</c> returns the
///     <b>metadata</b> and <c>GET .../attachments/{{attachmentId}}/content</c> returns the <b>file</b>,
///     streamed from <see cref="Pragmatic.Storage.IFileStorage"/> with the recorded content type and file
///     name. Both are gated on the same <c>attachments.read</c> permission and both are scoped to the
///     parent in the route. The metadata DTO deliberately does <b>not</b> expose <c>StorageUri</c>: it is
///     an infrastructure detail (provider and bucket layout, and on some providers a reachable URL).
///     </para>
///     <para>
///     <b>Deletion is a soft delete and never touches storage.</b> The generated delete action sets
///     <c>IsDeleted</c>/<c>DeletedAt</c>/<c>DeletedBy</c> on the metadata row and leaves the file in
///     <see cref="Pragmatic.Storage.IFileStorage"/> — on purpose, because a soft delete is reversible and
///     removing the bytes would make a restore impossible.
///     </para>
///     <para>
///     Set <see cref="PurgeDeletedAfterDays"/> to reclaim that storage: the generator then emits a
///     <c>[RecurringJob]</c> that deletes the blob <b>first</b> and the metadata row second. The row is
///     the only pointer to the file, so dropping it first and then crashing leaks a blob nobody can ever
///     find again; in the other order a crash just leaves the row for the next run to retry. Left at its
///     default the option generates nothing and <b>storage grows until the application reclaims it</b>:
///     deciding when a soft-deleted attachment is beyond recall is a business decision the attribute
///     cannot make.
///     </para>
///     <code>
///     // Blobs of attachments soft-deleted more than 30 days ago are purged nightly at 03:00.
///     [HasAttachments(PurgeDeletedAfterDays = 30)]
///     public partial class Order : IEntity { }
///     </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HasAttachmentsAttribute : Attribute
{
    /// <summary>
    ///     Maximum number of attachments per entity. <c>0</c> = unlimited; negative values are treated as unlimited.
    ///     Default: 20. The SG-generated upload action enforces this limit at runtime before writing to storage,
    ///     and holds it under concurrency: with a limit set the upload runs in a serializable transaction, so two
    ///     simultaneous uploads for the last slot cannot both succeed — the refused one gets <c>409 Conflict</c>.
    /// </summary>
    public int MaxPerEntity { get; set; } = 20;

    /// <summary>
    ///     Maximum file size in bytes. <c>0</c> = no size limit; negative values are treated as no limit.
    ///     Default: 10 MB (10 485 760 bytes). The SG-generated upload action enforces this limit at runtime.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10_485_760;

    /// <summary>
    ///     Allowed file extensions, comma-separated (e.g. <c>".pdf,.jpg,.png"</c>).
    ///     Empty string = allow all extensions <b>except</b> those a browser executes in the origin
    ///     that serves them (<c>.html</c>, <c>.svg</c>, <c>.js</c>, …). Naming one of those here is an
    ///     explicit opt-in and lifts the restriction for it.
    ///     <para>Parsing contract enforced by the SG-generated upload action:</para>
    ///     <list type="bullet">
    ///         <item><description>Each entry is trimmed of whitespace before comparison.</description></item>
    ///         <item><description>Comparison is case-insensitive (e.g. <c>.PDF</c> matches <c>.pdf</c>).</description></item>
    ///         <item><description>Leading dot is optional: <c>pdf</c> and <c>.pdf</c> are equivalent.</description></item>
    ///     </list>
    /// </summary>
    public string AllowedExtensions { get; set; } = "";

    /// <summary>Storage container name. Default: parent entity type name in lowercase.</summary>
    public string? Container { get; set; }

    /// <summary>Override sub-boundary name. Default: {ParentTypeName}Attachments.</summary>
    public string? SubBoundary { get; set; }

    /// <summary>
    ///     Retention window, in days, for soft-deleted attachments. <c>0</c> (default) or any negative
    ///     value means <b>no purge</b>: nothing extra is generated and the behaviour is exactly what it
    ///     was before the option existed.
    ///     <para>
    ///     When set to a positive number the SG emits <c>Purge{Parent}AttachmentsJob</c>, a
    ///     <c>[RecurringJob]</c> (schedule: <see cref="PurgeCron"/>) that finds every attachment with
    ///     <c>IsDeleted</c> and a <c>DeletedAt</c> older than the window — via <c>IgnoreQueryFilters()</c>,
    ///     since the generated soft-delete filter hides them — then, per row, deletes the blob from
    ///     <see cref="Pragmatic.Storage.IFileStorage"/> and only afterwards removes the row.
    ///     </para>
    ///     <para>
    ///     A row whose blob cannot be deleted is left in place and retried on the next run, so one
    ///     unreachable file never blocks the rest and never fails the job.
    ///     </para>
    /// </summary>
    public int PurgeDeletedAfterDays { get; set; }

    /// <summary>
    ///     Cron schedule of the generated purge job. Default: <c>"0 3 * * *"</c> (daily at 03:00).
    ///     Ignored unless <see cref="PurgeDeletedAfterDays"/> is positive. An empty or blank value
    ///     falls back to the default rather than silently disabling the job.
    /// </summary>
    public string PurgeCron { get; set; } = "0 3 * * *";

    /// <summary>
    ///     Bounds of the thumbnail derived from an uploaded image. <c>0</c> on either (the default)
    ///     means <b>no thumbnail</b>, and nothing extra is generated.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Set both and the generated upload action derives a thumbnail <b>where the file is
    ///         stored</b> — decoded once, resized within these bounds preserving the aspect ratio, and
    ///         written beside the original. A resize on the read path would be a resize per viewer.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>The thumbnail is derived, and <see cref="AttachmentBase{TEntityId}.ThumbnailUri" />
    ///         is nullable because of it.</b> It is null for an attachment that is not an image, null
    ///         for everything uploaded before this was switched on, and losing it is not data loss:
    ///         re-deriving it from the original is always possible. The download of the attachment
    ///         itself never serves it.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>This turns on a native dependency.</b> The derivation is emitted only when the
    ///         compilation references <c>Pragmatic.Imaging</c>, which P/Invokes a Rust library shipped
    ///         under <c>runtimes/{rid}/native/</c> — so the runtime identifier being deployed has to be
    ///         one that package carries. Declared without that reference, nothing is generated and the
    ///         generator says so (<c>PRAG2651</c>) rather than leaving an attribute that reads as
    ///         configured and does nothing.
    ///     </para>
    ///     <para>
    ///         ⚠️ A file whose extension names an image format and whose bytes do not decode is
    ///         <b>refused at upload</b>. It would not decode later either, and the difference between
    ///         the two moments is whether the failure has a caller to tell. A file that does not claim
    ///         to be an image never reaches the decoder.
    ///     </para>
    ///     <code>
    ///     [HasAttachments(ThumbnailMaxWidth = 240, ThumbnailMaxHeight = 240)]
    ///     public partial class Story : IEntity { }
    ///     </code>
    /// </remarks>
    public int ThumbnailMaxWidth { get; set; }

    /// <inheritdoc cref="ThumbnailMaxWidth" />
    public int ThumbnailMaxHeight { get; set; }
}
