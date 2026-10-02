using Pragmatic.Persistence.Entity;

namespace Pragmatic.Comments;

/// <summary>
///     Base class for generated comment entities.
///     The source generator creates <c>{Entity}Comment : CommentBase&lt;TEntityId&gt;</c>
///     for each entity decorated with <see cref="HasCommentsAttribute" />.
///     Implements <see cref="IEntity"/> so it works with <c>IRepository</c>.
/// </summary>
/// <typeparam name="TEntityId">The type of the parent entity's primary key.</typeparam>
public abstract class CommentBase<TEntityId> : IEntity
    where TEntityId : notnull
{
    // ── Identity ────────────────────────────────────────────────────────

    /// <summary>Comment unique identifier (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>IEntity implementation — maps to Id.</summary>
    public Guid PersistenceId { get => Id; set => Id = value; }

    /// <summary>FK to the parent entity. Set by the SG-generated constructor; never null at runtime.</summary>
    public TEntityId ParentEntityId { get; init; } = default!; // ASSUMPTION: SG always sets this via constructor; default! suppresses null warning for value-type TEntityId

    // ── Content ─────────────────────────────────────────────────────────

    /// <summary>Comment text content. Settable: the SG-generated update action assigns it post-construction.</summary>
    public string Content { get; set; } = string.Empty;

    // ── Author ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Identifier of the user who authored this comment, taken from <c>ICurrentUser.Id</c>
    ///     when the comment was added. Null only when the current user had no id.
    /// </summary>
    public string? AuthorId { get; init; }

    /// <summary>Display name of the author at the time of posting.</summary>
    public string? AuthorName { get; init; }

    // ── Threading ───────────────────────────────────────────────────────

    /// <summary>
    ///     FK to a parent comment for threaded replies. Null if top-level comment.
    ///     Only used when <see cref="HasCommentsAttribute.AllowReplies" /> is true.
    /// </summary>
    public Guid? ReplyToId { get; init; }

    // ── Lifecycle ───────────────────────────────────────────────────────

    /// <summary>Moderation/lifecycle status. Default: <see cref="CommentStatus.Visible" />.</summary>
    public CommentStatus Status { get; set; } = CommentStatus.Visible;

    /// <summary>
    ///     Visibility level: Public or Internal (staff-only).
    ///     Only meaningful when <see cref="HasCommentsAttribute.SupportInternalNotes" /> is enabled.
    /// </summary>
    public CommentVisibility Visibility { get; set; } = CommentVisibility.Public;

    /// <summary>Whether this comment has been edited after initial creation.</summary>
    public bool IsEdited { get; set; }

    // ── Audit ───────────────────────────────────────────────────────────

    /// <summary>When this comment was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this comment was last edited. Null if never edited.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Who last edited this comment. Null if never edited.</summary>
    public string? UpdatedBy { get; set; }

    // ── Soft delete ─────────────────────────────────────────────────────

    /// <summary>Whether this comment has been soft-deleted.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>When this comment was soft-deleted.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Who soft-deleted this comment.</summary>
    public string? DeletedBy { get; set; }

    // ── Extensibility ───────────────────────────────────────────────────

    /// <summary>
    ///     JSON column for custom metadata (mentions, rich content refs, etc.).
    ///     Schema-free — consumers can store arbitrary data without altering the table.
    ///     Max length: 4000 characters — enforced via the SG-generated EF Core entity configuration.
    /// </summary>
    public string? Metadata { get; set; }
}
