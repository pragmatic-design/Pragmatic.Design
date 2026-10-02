namespace Pragmatic.Actions.Commit;

/// <summary>
///     How the invocations of one action chain reach the database.
/// </summary>
/// <remarks>
///     <para>
///         Measured, so the choice is a number rather than a preference: three mutations invoked in a
///         loop cost three transactions under <see cref="PerStep" /> and one under <see cref="Once" />.
///     </para>
///     <para>
///         Two members, and no third: a boundary is the transaction boundary, so there is nothing wider
///         for this to describe. A chain that needs several saves inside one explicit transaction
///         opens it through <c>IUnitOfWork.BeginTransactionAsync</c>.
///     </para>
/// </remarks>
public enum CommitMode
{
    /// <summary>
    ///     One commit for the whole chain, performed by the outermost invoker that owns the unit of work.
    /// </summary>
    /// <remarks>
    ///     Nested invocations that resolve the <b>same</b> unit of work stage their writes and defer
    ///     their events; the root saves once. A step belonging to another boundary still commits itself —
    ///     nobody else can save its <c>DbContext</c>, and pretending otherwise is how rows get lost.
    /// </remarks>
    Once = 0,

    /// <summary>
    ///     Every invocation commits on its own, as an independent unit of work.
    /// </summary>
    /// <remarks>
    ///     The right choice when the steps are genuinely independent — an import where each row stands
    ///     alone and a failure on row 40 must not discard the first thirty-nine.
    /// </remarks>
    PerStep = 1
}
