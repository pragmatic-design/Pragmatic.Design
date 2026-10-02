using Pragmatic.Result;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     A rule the database enforced, carried as a domain error instead of a raw provider exception.
/// </summary>
/// <remarks>
///     <para>
///         A unique index, a foreign key, a null or length constraint: these are rules the application
///         declared — <c>[LogicKey]</c>, a relation, <c>[Required]</c> — and the database is simply
///         where they are enforced. Reaching the caller as an unhandled provider exception makes a
///         declared rule look like a crash: a 500, with a stack trace in the response, for something the
///         application knew was possible.
///     </para>
///     <para>
///         The unit of work classifies the provider's exception and throws this instead, carrying the
///         error the rule deserves. The host's exception mapping turns it into that error's status, and
///         an action that wants to react — an import reporting which row was refused — can catch it and
///         read <see cref="Error" /> rather than parse a message.
///     </para>
///     <para>
///         It stays an <b>exception</b> rather than becoming a <c>Result</c>: it is raised from
///         <c>SaveChangesAsync</c>, which returns a row count, and every caller between there and the
///         action would otherwise have to thread a failure it cannot act on.
///     </para>
/// </remarks>
/// <param name="error">The rule that was violated, as a domain error.</param>
/// <param name="innerException">The provider exception it was classified from.</param>
public sealed class PersistenceRuleViolationException(IError error, Exception? innerException = null)
    : Exception(error?.Title ?? "A database rule was violated.", innerException)
{
    /// <summary>The violated rule, ready to be returned or rendered.</summary>
    public IError Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}
