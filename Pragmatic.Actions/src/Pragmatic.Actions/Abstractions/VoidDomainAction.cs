using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for void domain actions that don't return a value.
/// </summary>
/// <example>
///     <code>
/// public partial class SendEmail : VoidDomainAction&lt;ValidationError&gt;
/// {
///     public required string To { get; init; }
///     public required string Subject { get; init; }
///
///     public override async Task&lt;VoidResult&lt;IError&gt;&gt; Execute(CancellationToken ct)
///     {
///         // Send email
///         return Success;
///     }
/// }
/// </code>
/// </example>
public abstract class VoidDomainAction : IVoidExecutable
{
    /// <summary>
    ///     Returns a successful void result.
    ///     Use this for cleaner syntax: <c>return Success;</c>
    /// </summary>
    protected static VoidResult<IError> Success => VoidResult<IError>.Success();

    /// <inheritdoc />
    public abstract Task<VoidResult<IError>> Execute(CancellationToken ct = default);
}
