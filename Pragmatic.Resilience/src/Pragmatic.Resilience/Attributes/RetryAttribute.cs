namespace Pragmatic.Resilience.Attributes;

/// <summary>
///     Declares that the decorated <c>[Job]</c>, <c>[RecurringJob]</c> or <c>[MessageHandler]</c> is
///     retried on failure, and how. On any other class nothing reads it (PRAG0464); a domain action
///     takes <c>[ResiliencePolicy]</c>.
/// </summary>
/// <remarks>
///     <para>
///         One declaration, read by whichever engine owns the class. A job retry is a lease and a
///         durable reschedule; a message retry is a redelivery. Those are different operations and
///         stay in their own modules — but «how many attempts, what backoff, what base delay» is one
///         question, and it gets one answer.
///     </para>
///     <para>
///         ⚠️ <b>This attribute carries no defaults, and that is deliberate.</b> A property
///         initializer never reaches metadata — a reader sees only what the caller wrote — so a
///         default declared here would be decorative while the number that actually applies would be
///         the reading engine's. Leaving them out keeps a job's base delay and a redelivery's from
///         being decided by whichever module got to name the attribute first.
///     </para>
///     <para>
///         ⚠️ <b>Unwritten is not the same as a written zero.</b> A property you leave out is absent
///         from metadata and the engine substitutes its own value; a property you set to <c>0</c> is a
///         value you chose, and an engine may reject it — the job engine reports <c>PRAG2504</c> for
///         <c>MaxAttempts = 0</c>. Reading this attribute through the CLR cannot tell the two apart,
///         because an unwritten property is simply zero there; only a reader looking at the
///         attribute's metadata can.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RetryAttribute : Attribute
{
    /// <summary>
    ///     Total executions allowed, including the first. Leave it out and the engine decides.
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    ///     How the delay grows between attempts. Leave it out — it then reads as
    ///     <see cref="BackoffStrategy.Unspecified" /> — and the engine decides.
    /// </summary>
    public BackoffStrategy Strategy { get; set; }

    /// <summary>
    ///     Base delay in milliseconds. Leave it out and the engine decides.
    /// </summary>
    public int BaseDelayMs { get; set; }
}
