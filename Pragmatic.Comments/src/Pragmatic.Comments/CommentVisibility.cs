namespace Pragmatic.Comments;

/// <summary>
///     Visibility level of a comment.
///     Only relevant when <see cref="HasCommentsAttribute.SupportInternalNotes"/> is enabled.
/// </summary>
public enum CommentVisibility
{
    /// <summary>Visible to all users with read access.</summary>
    Public,

    /// <summary>
    ///     Read and written only by callers holding the generated
    ///     <c>{boundary}.{entity-kebab}.comments.view-internal</c> permission: the generated list filters
    ///     these rows out for everyone else, a read by id answers 404, and adding one without the
    ///     permission answers 403.
    /// </summary>
    Internal,
}
