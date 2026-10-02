namespace Pragmatic.Privacy;

/// <summary>
///     One operation that processes personal data, and what it does with it.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ProcessingActivity" /> describes a <b>type</b>: what personal data it holds and how
///         each field is erased. Article 30 asks about <b>activities</b>, and this is that half — the
///         operations through which the data is actually processed.
///     </para>
///     <para>
///         <b>Why the distinction earns its keep.</b> A purpose belongs to an operation, not to a type.
///         "Why do we hold <c>Member.DisplayName</c>" has no single answer, because different operations
///         touch it for different reasons; "why does <c>ImportMembersAction</c> process it" has one. So a
///         register keyed by type can only ever ask the controller a question they cannot answer
///         precisely, while one keyed by operation asks a list of questions each of which somebody can
///         actually sit down and answer.
///     </para>
///     <para>
///         Derived at compile time from the operations this assembly declares and the entities they name,
///         so it costs nothing at run time and cannot drift from the code. What it cannot derive is the
///         purpose, which is left null and reported — see <see cref="NeedsDeclaredPurpose" />.
///     </para>
/// </remarks>
/// <param name="OperationType">The query or mutation type, fully qualified.</param>
/// <param name="Access">Whether the operation reads the data or writes it.</param>
/// <param name="EntityType">The entity holding the personal data this operation touches.</param>
/// <param name="Categories">The categories of personal data on that entity.</param>
/// <param name="Route">The HTTP route the operation is reachable at, when it has one.</param>
/// <param name="Recorded">
///     Whether each execution of this operation is written to the audit trail.
/// </param>
/// <param name="Purpose">
///     Why the processing happens. <see langword="null" /> when the code alone cannot say, which is
///     always: a purpose is a decision, not a fact about the source.
/// </param>
public sealed record ProcessingOperation(
    string OperationType,
    ProcessingAccess Access,
    string EntityType,
    IReadOnlyList<string> Categories,
    string? Route = null,
    bool Recorded = false,
    string? Purpose = null)
{
    /// <summary>
    ///     True when this entry still needs a purpose before the register is complete.
    /// </summary>
    /// <remarks>
    ///     Reported rather than filled in with something plausible, for the same reason
    ///     <see cref="ProcessingActivity.NeedsDeclaredPurpose" /> is: a register that invents its own
    ///     purposes reads as authoritative and is not.
    /// </remarks>
    public bool NeedsDeclaredPurpose => string.IsNullOrWhiteSpace(Purpose);
}
