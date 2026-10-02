using Pragmatic.Result;

namespace Pragmatic.Comments;

/// <summary>
///     Hook interface for customizing comment behavior per entity.
///     Implement this interface and register it in DI to intercept comment operations.
///     <para>
///     The SG-generated comment actions call these hooks automatically when a policy is registered.
///     </para>
/// </summary>
/// <typeparam name="TEntityId">The parent entity's id type.</typeparam>
public interface ICommentPolicy<TEntityId>
    where TEntityId : notnull
{
    /// <summary>
    ///     Called before a comment is added.
    ///     Return <c>Ok</c> to allow, or <c>Fail</c> with a <see cref="CommentRejectedError"/>
    ///     to reject — the SG-generated action surfaces the error reason to the caller.
    /// </summary>
    /// <param name="parentId">The parent entity id.</param>
    /// <param name="content">The comment content.</param>
    /// <param name="authorId">The author id (null if anonymous).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<VoidResult<CommentRejectedError>> CanAddAsync(TEntityId parentId, string content, string? authorId, CancellationToken ct = default)
        => Task.FromResult(VoidResult<CommentRejectedError>.Success());

    /// <summary>
    ///     Called after a comment is successfully added.
    /// </summary>
    /// <param name="parentId">The parent entity id.</param>
    /// <param name="commentId">The new comment id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnAddedAsync(TEntityId parentId, Guid commentId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    ///     Called before a comment is deleted.
    ///     Return <c>Ok</c> to allow, or <c>Fail</c> with a <see cref="CommentRejectedError"/> to reject.
    /// </summary>
    /// <param name="commentId">The comment id.</param>
    /// <param name="requesterId">The user requesting deletion.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<VoidResult<CommentRejectedError>> CanDeleteAsync(Guid commentId, string? requesterId, CancellationToken ct = default)
        => Task.FromResult(VoidResult<CommentRejectedError>.Success());

    /// <summary>
    ///     Called before a comment is edited.
    ///     Return <c>Ok</c> to allow, or <c>Fail</c> with a <see cref="CommentRejectedError"/> to reject.
    /// </summary>
    /// <param name="commentId">The comment id.</param>
    /// <param name="requesterId">The user requesting edit.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<VoidResult<CommentRejectedError>> CanEditAsync(Guid commentId, string? requesterId, CancellationToken ct = default)
        => Task.FromResult(VoidResult<CommentRejectedError>.Success());
}
