namespace Pragmatic.Privacy;

/// <summary>
///     Decides how long a particular record may be kept.
/// </summary>
/// <remarks>
///     <para>
///         <b>Retention is a property of the lawful basis, not of the type.</b> The same
///         <c>Order</c> is kept for ten years under a fiscal obligation and only until withdrawal under
///         consent — so a period declared on the class would be wrong for half its rows. This is why
///         <c>[PersonalData]</c> carries classification and not policy: the attribute says what the data
///         <em>is</em>, the resolver says what may be done with it.
///     </para>
///     <para>
///         Which means the answer depends on runtime facts — the purpose the record was collected for,
///         and whether the basis still holds — and cannot be computed at compile time however much that
///         would simplify the generator.
///     </para>
/// </remarks>
public interface IRetentionPolicyResolver
{
    /// <summary>
    ///     Resolves the retention for one record.
    /// </summary>
    /// <param name="context">What is known about the record and why it is held.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<RetentionDecision> ResolveAsync(RetentionContext context, CancellationToken ct = default);
}
