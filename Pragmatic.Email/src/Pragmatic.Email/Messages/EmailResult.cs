namespace Pragmatic.Email;

/// <summary>
///     Result of an email send operation.
/// </summary>
public sealed record EmailResult(bool Success, string? MessageId, string? ErrorMessage)
{
    public static EmailResult Succeeded(string messageId) => new(true, messageId, null);

    /// <summary>
    ///     Creates a failed result. Pass <paramref name="messageId"/> whenever it is known: a caller
    ///     correlating a failure with its own tracking record cannot do so from an error string alone.
    /// </summary>
    public static EmailResult Failed(string errorMessage, string? messageId = null)
        => new(false, messageId, errorMessage);
}
