using Casework.Intake.Enums;

namespace Casework.Intake.Entities;

/// <summary>The rules a case is read by, beyond its id.</summary>
public static partial class CaseSpecifications
{
    /// <summary>
    ///     The cases still waiting for an answer that was due before <paramref name="now" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both conditions, and neither is redundant. The status, because a case that was decided or
    ///         already expired is not waiting for anything — and the deadline, because a case in
    ///         verification whose window has not run out is exactly what the process is for.
    ///     </para>
    ///     <para>
    ///         The tenant is not in the rule, as in every specification here: the filter is, and it comes
    ///         from the scope the caller opened. ⚠️ The job that uses this reads <b>across</b> tenants on
    ///         purpose and says so at the call site — which is the one place in this example where that
    ///         is true, and the reason it is written there rather than hidden in here.
    ///     </para>
    ///     <para>
    ///         ⚠️ <paramref name="now" /> is a value, computed by the caller from the application's clock
    ///         and captured: <c>DateTimeOffset.UtcNow</c> inside the expression would be a method call for
    ///         the provider to translate, and "what time is it" is not the database's question to answer.
    ///     </para>
    /// </remarks>
    public static Specification<Case> AwaitingAnAnswerDueBefore(DateTimeOffset now)
        => Spec<Case>.Where(@case =>
            @case.Status == CaseStatus.InVerification
            && @case.VerificationDueOn != null
            && @case.VerificationDueOn < now);
}
