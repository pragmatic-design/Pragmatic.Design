using Pragmatic.Identity;

namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a domain action that runs <b>on behalf of someone else</b>: the invoker opens a
///     delegation for the whole execution, and closes it after.
/// </summary>
/// <remarks>
///     <para>
///         The subject is named by a property on the action, so the action carries who it acts for the
///         same way it carries anything else it needs. Everything inside — permission checks in nested
///         calls, row filters, ownership stamping, cache keys, the audit trail — then follows from the
///         composed authority instead of from the caller's alone.
///     </para>
///     <para>
///         ⚠️ <b>The action's own <c>[RequirePermission]</c> is still evaluated against the caller</b>,
///         before the scope opens. Who may open a delegation and what may be done inside one are
///         different questions, and merging them would make declaring this attribute enough to let
///         anyone execute the action for anyone. The action's permission is the door; this is what
///         happens after it.
///     </para>
///     <para>
///         ⚠️ Which is also the limit worth knowing: with no grant store, <b>the action's permission is
///         the only thing standing between a caller and acting for an arbitrary subject</b>. Whoever
///         may run the action may run it for whoever they name in the subject property. Gate the
///         action accordingly, and read <c>pragmatic-use-delegation</c> before relying on it.
///     </para>
///     <para>
///         Writing <c>ActAs</c> by hand in the action body is not the same thing and is a step
///         backwards: the body runs after authorization has already decided, so the action would be
///         authorized as the caller and executed as the subject.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [RequirePermission(WorkPermissions.WorkItem.Update)]
/// [StartsDelegation(nameof(ForMemberId), Purpose = "agent takeover")]
/// public partial class TakeOverWorkItemAction
/// {
///     public required Guid WorkItemId { get; init; }
///     public required string ForMemberId { get; init; }
/// }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StartsDelegationAttribute : Attribute
{
    /// <summary>
    ///     Names the action property holding the subject's id.
    /// </summary>
    /// <param name="subjectPropertyName">
    ///     Use <c>nameof</c>: the compiler then checks the name, and the generator checks the type
    ///     (<c>PRAG0423</c> otherwise).
    /// </param>
    public StartsDelegationAttribute(string subjectPropertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(subjectPropertyName);
        SubjectPropertyName = subjectPropertyName;
    }

    /// <summary>The action property holding the subject's id.</summary>
    public string SubjectPropertyName { get; }

    /// <summary>Why the delegation exists. It reaches the audit trail, so write it for a reader.</summary>
    public string? Purpose { get; init; }

    /// <summary>
    ///     How authority composes. <see cref="DelegationPolicy.Intersection" /> by default: the action
    ///     may do neither more than the caller nor more than the subject.
    /// </summary>
    public DelegationPolicy Policy { get; init; } = DelegationPolicy.Intersection;
}
