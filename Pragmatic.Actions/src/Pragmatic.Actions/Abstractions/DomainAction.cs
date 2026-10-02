using Pragmatic.Result;

namespace Pragmatic.Actions.Abstractions;

/// <summary>
///     Base class for domain actions that return a value.
///     Inherit from this class to create actions without explicit error types.
/// </summary>
/// <typeparam name="TReturn">The return type on success.</typeparam>
/// <example>
///     <code>
/// public partial class GetUser : DomainAction&lt;UserDto&gt;
/// {
///     public required Guid UserId { get; init; }
///
///     public override async Task&lt;Result&lt;UserDto, IError&gt;&gt; Execute(CancellationToken ct)
///     {
///         // Implementation
///     }
/// }
/// </code>
/// </example>
public abstract class DomainAction<TReturn> : IExecutable<TReturn>
{
    /// <inheritdoc />
    public abstract Task<Result<TReturn, IError>> Execute(CancellationToken ct = default);
}
