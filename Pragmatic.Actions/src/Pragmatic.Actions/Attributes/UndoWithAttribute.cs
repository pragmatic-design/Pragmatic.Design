namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Declares how to undo this action once it has committed, so a caller that fails afterwards does
///     not leave the work behind.
/// </summary>
/// <typeparam name="TCompensator">
///     A scoped service implementing <c>ICompensates&lt;TResult&gt;</c> for this action's return type,
///     or <c>ICompensatesVoid</c> for a void action.
/// </typeparam>
/// <remarks>
///     <para>
///         Each boundary commits separately, inner first. When an action declares a compensator, the
///         invoker registers the undo after the commit succeeds; if an outer action in the same request
///         then fails, its invoker runs the registered undos in reverse order before returning.
///     </para>
///     <para>
///         <b>Best effort, in-request, and that is the whole guarantee.</b> A crash between the inner
///         commit and the compensation leaves the work committed: nothing here is durable, nothing is
///         retried. That is the line where a saga starts, and this attribute does not pretend to cross
///         it. What it buys is the ordinary case — a failure that returns rather than kills the process
///         — which is the case PRAG0424 warns about.
///     </para>
///     <para>
///         A compensator that itself fails is logged at <c>Error</c> and reported to the caller: the
///         response says the system is inconsistent, rather than only that the operation failed.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [UndoWith&lt;RemoveIngestedText&gt;]
/// public partial class IngestTextAction : DomainAction&lt;IngestResult&gt;;
///
/// public sealed class RemoveIngestedText(IRepository&lt;Term&gt; terms) : ICompensates&lt;IngestResult&gt;
/// {
///     public Task&lt;VoidResult&lt;IError&gt;&gt; Undo(IngestResult committed, CancellationToken ct = default)
///     {
///         // remove what the ingestion added
///     }
/// }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UndoWithAttribute<TCompensator> : Attribute
    where TCompensator : class;
