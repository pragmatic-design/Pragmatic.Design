namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Declares that everything this action does happens in one database transaction.
/// </summary>
/// <remarks>
///     <para>
///         For a body the framework cannot see the shape of — a loop, a condition, a number of steps
///         known only at runtime. <c>[CompositeAction]</c> is the same guarantee for steps declared as
///         properties; this one is for the rest.
///     </para>
///     <para>
///         The invoker opens a transaction on its boundary's unit of work before the body runs and
///         commits it after. Each nested step still saves, which is the point: a later step can read
///         what an earlier one wrote. The cost is a round trip per step, where the default costs one in
///         total — so this is for chains that need the visibility, not for every chain.
///     </para>
///     <para>
///         Domain events wait for the commit rather than the save. Outside a transaction the two
///         coincide; inside one they do not, and a handler acting on a change that is then rolled back
///         is a defect rather than a trade-off.
///     </para>
///     <para>
///         ⚠️ <b>A boundary is the transaction boundary.</b> Calling another boundary from inside a
///         transactional action is <c>PRAG0426</c>, an error: its writes commit through their own unit
///         of work and no rollback here can reach them. Cross a boundary with a domain event, or
///         declare <c>[UndoWith&lt;T&gt;]</c> on the step and accept what that costs.
///     </para>
///     <para>
///         ⚠️ <b>The body can run twice.</b> A generated host configures SQL Server and PostgreSQL to retry
///         transient failures, and a retrying strategy refuses a transaction opened outside it, so the
///         invoker runs the whole invocation inside it. A transient failure before the commit runs it
///         again from a cleared change tracker. So the body must have no effect outside its transaction:
///         mail, messages and events go through the outbox, which is transactional; a direct HTTP call or a
///         file write runs again. What follows the commit runs once. No diagnostic checks this, because
///         whether a body reaches outside the database is not something the generator can read off it.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [Transactional]
/// public partial class CloseAccountAction : DomainAction&lt;int&gt;
/// {
///     public override async Task&lt;Result&lt;int, IError&gt;&gt; Execute(CancellationToken ct = default)
///     {
///         foreach (var order in open)               // as many steps as there are orders
///             await _archive.InvokeAsync(new ArchiveOrderAction { Id = order.Id }, ct);
///
///         return open.Count;                         // one transaction, committed here
///     }
/// }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TransactionalAttribute : Attribute;
