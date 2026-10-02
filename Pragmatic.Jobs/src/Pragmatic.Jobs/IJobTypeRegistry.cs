namespace Pragmatic.Jobs;

/// <summary>
///     AOT-safe job type registry. SG-generated switch expression
///     maps job FQN → parameter serialization/deserialization.
/// </summary>
public interface IJobTypeRegistry
{
    /// <summary>Whether this registry can execute the job type.</summary>
    /// <remarks>
    ///     ⚠️ Asked before anything else, and it is the member that makes several registries
    ///     composable: every other method answers <see langword="null" /> or zero for a type it does
    ///     not know, which is the same answer as "knows it, and it declares no retry policy". One
    ///     value meaning two things is not a question a composite can dispatch on.
    /// </remarks>
    bool Knows(string jobTypeFqn);

    /// <summary>Deserializes job parameters from JSON by job type FQN.</summary>
    object? DeserializeParameters(string jobTypeFqn, string? json);

    /// <summary>Serializes job parameters to JSON by job type FQN.</summary>
    string? SerializeParameters(string jobTypeFqn, object? parameters);

    /// <summary>Resolves the job from the caller's scope and executes the job.</summary>
    Task ExecuteAsync(string jobTypeFqn, string? parametersJson, JobContext context, IServiceProvider serviceProvider, CancellationToken ct);

    /// <summary>
    ///     Gets the retry policy declared by <c>[Retry]</c> on the job type, or <c>null</c> when the
    ///     job declares none — in which case <c>JobsOptions.DefaultMaxRetries</c> applies.
    /// </summary>
    JobRetryPolicy? GetRetryPolicy(string jobTypeFqn);

    /// <summary>
    ///     Gets the FQN of the job declared by <c>[Continuation&lt;T&gt;]</c> on the job type, to be
    ///     enqueued after it completes successfully, or <c>null</c> when none is declared.
    /// </summary>
    string? GetContinuationJobType(string jobTypeFqn);

    /// <summary>Gets the declared scheduling priority for the job type (0 = default).</summary>
    int GetPriority(string jobTypeFqn);

    /// <summary>
    ///     Gets the maximum instances of the job type a single host may run concurrently
    ///     (0 = unbounded beyond the global worker count).
    /// </summary>
    int GetMaxConcurrency(string jobTypeFqn);
}
