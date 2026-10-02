namespace Pragmatic.Jobs.Attributes;

/// <summary>
///     Declares that a continuation job should be enqueued when this job completes successfully.
///     The SG validates that <typeparamref name="TNextJob"/> implements <c>IJob</c> or <c>IJob&lt;T&gt;</c>.
/// </summary>
/// <typeparam name="TNextJob">The next job to run on completion.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContinuationAttribute<TNextJob> : Attribute where TNextJob : class;
