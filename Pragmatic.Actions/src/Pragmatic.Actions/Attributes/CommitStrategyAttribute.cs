using Pragmatic.Actions.Commit;

namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Chooses how this action's chain of invocations reaches the database.
/// </summary>
/// <remarks>
///     <para>
///         Written only to deviate: without it a chain commits once, at the outermost invoker that owns
///         the unit of work. The reason to say otherwise is an import whose rows are independent, where
///         a failure on row 40 must not discard the first thirty-nine —
///         <see cref="CommitMode.PerStep" />.
///     </para>
///     <para>
///         It governs one unit of work — the action's own boundary. A step in another boundary commits
///         through its own, whatever is written here: no invoker can save a <c>DbContext</c> it does not
///         hold, and a mode that pretended otherwise would lose those rows silently. Crossing that line
///         atomically is what <c>PRAG0424</c> reports and what <c>[UndoWith&lt;T&gt;]</c> or a saga
///         answers.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [CommitStrategy(CommitMode.PerStep)]
/// public partial class ImportRowsAction : VoidDomainAction;
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CommitStrategyAttribute(CommitMode mode) : Attribute
{
    /// <summary>How the chain reaches the database.</summary>
    public CommitMode Mode { get; } = mode;
}
