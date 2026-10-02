namespace Pragmatic.Comments;

/// <summary>
///     Lifecycle status of a comment entity.
/// </summary>
public enum CommentStatus
{
    /// <summary>Comment is visible to all allowed viewers.</summary>
    Visible,

    /// <summary>Comment is pending moderator approval.</summary>
    PendingApproval,

    /// <summary>Comment was rejected by a moderator.</summary>
    Rejected,

    /// <summary>Comment was hidden (e.g. flagged by users or staff).</summary>
    Hidden,
}
