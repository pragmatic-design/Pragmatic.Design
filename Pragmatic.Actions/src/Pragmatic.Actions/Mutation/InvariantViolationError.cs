using Pragmatic.Result;

namespace Pragmatic.Actions.Mutation;

/// <summary>
///     Error returned when an aggregate invariant (an entity method marked <c>[Invariant]</c>) does not
///     hold after a mutation is applied. The mutation is rejected before it is persisted.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It is a refusal on the <b>mutation path</b>. A <c>[DomainAction]</c> that loads the same
///         aggregate and changes it writes the same rows with none of those methods run, and so does a
///         repository <c>Add</c>/<c>Remove</c>.
///     </para>
///     <para>
///         It derives from <see cref="Error" /> so its message can be translated. The localization
///         resolver asks <c>context is Error</c> and reads that error's <see cref="Error.MessageKey" />;
///         while this type implemented <c>IError</c> directly it took the fallback derived from
///         <see cref="Code" /> — <c>error.invariant.violation</c>, one text for every invariant in the
///         application, which cannot say which rule refused. An <c>[Invariant]</c>'s sentence was the only
///         text in the stack with no way to be localized.
///     </para>
/// </remarks>
/// <param name="Invariant">The name of the violated invariant method.</param>
/// <param name="Message">Human-readable explanation (from <c>[Invariant("…")]</c>, or a default).</param>
/// <param name="DeclaredMessageKey">
///     The key <c>[Invariant(MessageKey = …)]</c> names, or <see langword="null" /> when the rule names
///     none — and then the key is the one derived from <see cref="Code" />, as before.
/// </param>
public sealed record InvariantViolationError(string Invariant, string Message, string? DeclaredMessageKey = null)
    : Error
{
    /// <inheritdoc />
    public override string Code => "INVARIANT_VIOLATION";

    /// <inheritdoc />
    public override int StatusCode => 422;

    /// <inheritdoc />
    public override string Title => string.IsNullOrEmpty(Message)
        ? $"Invariant '{Invariant}' was violated."
        : Message;

    /// <inheritdoc />
    public override string MessageKey => DeclaredMessageKey ?? base.MessageKey;
}
