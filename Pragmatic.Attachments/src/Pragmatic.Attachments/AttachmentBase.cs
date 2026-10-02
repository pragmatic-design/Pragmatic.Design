using Pragmatic.Persistence.Entity;

namespace Pragmatic.Attachments;

/// <summary>
///     Base class for generated attachment metadata entities.
///     The SG creates <c>{Entity}Attachment : AttachmentBase&lt;TEntityId&gt;</c>
///     for each entity decorated with <see cref="HasAttachmentsAttribute" />.
///     <para>
///     Stores file metadata (name, size, content type, storage URI).
///     Actual file content is persisted via <see cref="Pragmatic.Storage.IFileStorage"/>.
///     </para>
/// </summary>
/// <typeparam name="TEntityId">The type of the parent entity's primary key.</typeparam>
public abstract class AttachmentBase<TEntityId> : IEntity, ISoftDelete
    where TEntityId : notnull
{
    /// <summary>Attachment unique identifier (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>IEntity implementation.</summary>
    public Guid PersistenceId { get => Id; set => Id = value; }

    /// <summary>FK to the parent entity.</summary>
    public TEntityId ParentEntityId { get; set; } = default!;

    /// <summary>Original file name (e.g. "contract.pdf").</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    ///     File size in bytes. Must be greater than or equal to zero.
    ///     A value of zero indicates an empty file; negative values are invalid and should be rejected by upload logic.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>MIME content type (e.g. "application/pdf").</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    ///     URI to the stored file (relative for local storage, absolute for cloud storage).
    ///     Used by <see cref="Pragmatic.Storage.IFileStorage"/> to retrieve the file.
    ///     <para>
    ///     Expected format: a non-empty string produced by the <c>IFileStorage.SaveAsync</c> implementation.
    ///     Consumers must not construct this value manually; it is set exclusively by the SG-generated upload action.
    ///     Maximum recommended length: 2 048 characters (aligned with common URL length limits).
    ///     </para>
    /// </summary>
    public string StorageUri { get; set; } = string.Empty;

    /// <summary>
    ///     URI of the thumbnail derived from this file, or <see langword="null" /> when there is none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>Derived, and never a substitute for <see cref="StorageUri" />.</b> Losing it is
    ///         not data loss — it can be re-derived from the original — and the download of the
    ///         attachment must never serve it in the original's place. It is a second, optional file
    ///         beside the record, not part of the record.
    ///     </para>
    ///     <para>
    ///         Null is the ordinary case and not an error: an attachment that is not an image has no
    ///         thumbnail, and neither does anything uploaded before
    ///         <c>[HasAttachments(ThumbnailMaxWidth = …, ThumbnailMaxHeight = …)]</c> was declared.
    ///     </para>
    /// </remarks>
    public string? ThumbnailUri { get; set; }

    /// <summary>Optional description or label for the attachment.</summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Subject identifier (user ID) of the principal who uploaded this attachment.
    ///     Set by the SG-generated upload action from <c>ICurrentUser.Id</c>.
    ///     This is a stable user ID, not a display name; resolve to a human-readable label at the presentation layer if needed.
    /// </summary>
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>When this attachment was uploaded.</summary>
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>Soft-deleted flag.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>When soft-deleted.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Who soft-deleted.</summary>
    public string? DeletedBy { get; set; }
}
