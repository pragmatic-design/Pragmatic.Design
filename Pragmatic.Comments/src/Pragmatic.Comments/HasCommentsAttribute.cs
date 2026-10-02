namespace Pragmatic.Comments;

/// <summary>
///     Marks an entity as having an associated comments collection.
///     This is a <b>Trait (Strada C)</b> — the SG generates the entire domain
///     feature in the consumer, with typed FK to the parent entity.
///     <para>
///     The SG generates per consumer entity:
///     </para>
///     <list type="bullet">
///         <item><description>A typed child entity <c>{Parent}Comment</c> with <c>{Parent}Id</c> FK (real, with cascade delete)</description></item>
///         <item><description>An EF Core <c>IEntityTypeConfiguration</c> for the comment entity</description></item>
///         <item><description>CRUD domain actions: <c>Add{Parent}Comment</c>, <c>Get{Parent}Comment</c>, <c>Update{Parent}Comment</c> (unless <see cref="AllowEditing"/> is <c>false</c>), <c>Delete{Parent}Comment</c></description></item>
///         <item><description>REST endpoints under the parent route: <c>/api/.../{{parentId}}/comments</c></description></item>
///         <item><description>A navigation property <c>ICollection&lt;{Parent}Comment&gt; Comments</c> on the parent entity</description></item>
///     </list>
///     <para>
///     No <c>ParentEntityType</c>/<c>ParentEntityId</c> polymorphic columns — only typed FK.
///     Each consumer entity gets its own comment table for full referential integrity.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HasCommentsAttribute : Attribute
{
    // ── Content ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Maximum length of comment content. Default: 2000.
    /// </summary>
    public int MaxLength { get; set; } = 2000;

    // ── Threading ────────────────────────────────────────────────────────

    /// <summary>
    ///     Whether replies (nested comments) are allowed. Default: true.
    /// </summary>
    public bool AllowReplies { get; set; } = true;

    // ── Editing ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Whether comment authors can edit their own comments. Default: true.
    ///     When <c>false</c> the SG generates neither <c>Update{Parent}CommentAction</c>
    ///     nor its <c>PUT</c> endpoint, so editing is not reachable at all.
    /// </summary>
    public bool AllowEditing { get; set; } = true;

    /// <summary>
    ///     Time window (in minutes) during which editing is allowed after creation.
    ///     <c>-1</c> means always editable (no time limit) and is the default; <c>0</c> closes the
    ///     window immediately, so a comment can never be edited after it is created.
    ///     Only has effect when <see cref="AllowEditing"/> is <c>true</c>.
    /// </summary>
    public int EditWindowMinutes { get; set; } = -1;

    // ── Moderation ───────────────────────────────────────────────────────

    /// <summary>
    ///     Whether new comments require moderator approval before becoming visible.
    ///     When true, new comments start with <see cref="CommentStatus.PendingApproval"/>.
    ///     Default: false.
    /// </summary>
    public bool RequireApproval { get; set; }

    // ── Visibility ───────────────────────────────────────────────────────

    /// <summary>
    ///     Whether internal/staff-only notes are supported alongside public comments.
    ///     When true, the <see cref="CommentVisibility"/> field becomes meaningful.
    ///     Default: false.
    /// </summary>
    public bool SupportInternalNotes { get; set; }

    // ── Boundary ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Override the sub-boundary name in the boundary interface.
    ///     Default: <c>{ParentTypeName}Comments</c> (e.g. "ReservationComments").
    /// </summary>
    public string? SubBoundary { get; set; }
}
