namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Records that this action may leave another boundary's writes committed when it fails, and that
///     this is acceptable.
/// </summary>
/// <remarks>
///     <para>
///         An action writes in its own boundary and calls an action in another. Each boundary has its
///         own <c>DbContext</c> and its own <c>SaveChanges</c>, so the inner call commits before the
///         outer one does — and if the outer action then fails, nothing rolls the inner commit back.
///         Measured on a real application: five rows survived a failure, pointing at a parent row that
///         was never written.
///     </para>
///     <para>
///         The generator cannot decide whether that matters to you. It can see the shape — you write,
///         and you call another boundary — and it can insist that the decision be written down. This
///         attribute is the recorded "yes, and it is fine": the alternative is to make the inner step
///         compensable, or to move the work into one boundary.
///     </para>
///     <para>
///         Silence is not a decision, which is why <c>PRAG0424</c> is a warning until one of the two
///         is present.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [AcceptsPartialWrites("The glossary keeps unreferenced candidates; a nightly job prunes them.")]
/// public partial class WriteStoryAction : DomainAction&lt;WriteStoryResult&gt; { }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AcceptsPartialWritesAttribute : Attribute
{
    /// <summary>Creates the record of the decision.</summary>
    /// <param name="reason">
    ///     Why the partial write is acceptable. Required: a decision without a reason is
    ///     indistinguishable from silencing the warning, which is the thing this exists to prevent.
    /// </param>
    public AcceptsPartialWritesAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Why the partial write is acceptable.</summary>
    public string Reason { get; }
}
