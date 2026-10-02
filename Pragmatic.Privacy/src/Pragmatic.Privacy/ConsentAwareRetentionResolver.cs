namespace Pragmatic.Privacy;

/// <summary>
///     Default <see cref="IRetentionPolicyResolver" />: consent-based processing lasts as long as the
///     consent does, everything else falls back to the declared default.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately small, and deliberately not clever. Retention periods come from law and from
///         contracts, and a framework that guessed them would be inventing legal advice. What it can do
///         without guessing is honour the one basis it can actually observe: whether consent still
///         stands.
///     </para>
///     <para>
///         Withdrawing consent makes the record eligible for deletion — it does not delete it. Something
///         held on a second basis stays, and that is the difference between a withdrawal and an erasure
///         request.
///     </para>
/// </remarks>
public sealed class ConsentAwareRetentionResolver(
    IConsentStore consents,
    string noticeVersion,
    TimeProvider timeProvider) : IRetentionPolicyResolver
{
    public async ValueTask<RetentionDecision> ResolveAsync(
        RetentionContext context, CancellationToken ct = default)
    {
        var granted = await consents
            .IsGrantedAsync(context.SubjectRef, context.Purpose, noticeVersion, ct)
            .ConfigureAwait(false);

        if (granted)
        {
            // While consent stands there is no end date to compute: the basis itself is what keeps the
            // record alive, and it ends when the subject says so rather than on a schedule.
            return RetentionDecision.Indefinite($"consent for '{context.Purpose}' under notice {noticeVersion}");
        }

        if (context.DeclaredDefault is { } declared)
        {
            return RetentionDecision.For(
                context, declared, $"declared retention for {context.EntityType}.{context.Purpose}");
        }

        // No consent and no declared period: nothing observable justifies keeping it. Saying so is
        // more useful than inventing a period — an unexplained default is how data outlives its purpose.
        _ = timeProvider;
        return RetentionDecision.NoLongerNeeded(
            $"no active consent for '{context.Purpose}' and no declared retention");
    }
}
