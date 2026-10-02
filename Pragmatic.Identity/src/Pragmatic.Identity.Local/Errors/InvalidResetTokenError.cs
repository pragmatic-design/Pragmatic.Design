using Pragmatic.Result;

namespace Pragmatic.Identity.Local.Errors;

/// <summary>Password reset token is invalid or expired.</summary>
public sealed record InvalidResetTokenError() : Error
{
    /// <inheritdoc />
    public override string Code => "INVALID_RESET_TOKEN";

    /// <inheritdoc />
    public override int StatusCode => 400;

    /// <inheritdoc />
    public override string Title => "Invalid or expired reset token";
}
