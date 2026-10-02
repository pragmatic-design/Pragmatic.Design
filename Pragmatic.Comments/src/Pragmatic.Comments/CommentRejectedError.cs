using Pragmatic.Result;

namespace Pragmatic.Comments;

/// <summary>
///     Returned by <see cref="ICommentPolicy{TEntityId}"/> methods when an operation is rejected by policy.
///     Maps to HTTP 422 Unprocessable Entity so the caller can surface a meaningful message.
/// </summary>
public sealed record CommentRejectedError : Error
{
    /// <inheritdoc />
    public override string Code => "COMMENT_REJECTED";

    /// <inheritdoc />
    public override int StatusCode => 422;

    /// <inheritdoc />
    public override string Title => "Comment operation rejected by policy.";

    /// <summary>Human-readable reason the operation was rejected.</summary>
    public string? Reason { get; init; }

    /// <inheritdoc />
    public override void WriteExtensions(IDictionary<string, object?> extensions)
    {
        if (Reason is not null)
            extensions["reason"] = Reason;
    }

    /// <summary>Creates a <see cref="CommentRejectedError"/> with the specified reason.</summary>
    public static CommentRejectedError Because(string reason) => new() { Reason = reason };
}
