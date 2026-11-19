using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>An application error with a status of its own, so a case is told apart by more than the framework's statuses.</summary>
public sealed record OrderLocked : Error
{
    /// <inheritdoc />
    public override string Code => "ORDER_LOCKED";

    /// <inheritdoc />
    public override int StatusCode => 423;

    /// <inheritdoc />
    public override string Title => "Locked";
}
