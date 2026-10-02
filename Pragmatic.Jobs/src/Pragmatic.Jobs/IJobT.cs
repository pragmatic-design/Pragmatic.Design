namespace Pragmatic.Jobs;

/// <summary>
///     A background job with typed parameters.
///     The SG generates AOT-safe serialization for <typeparamref name="TParams"/>.
/// </summary>
/// <typeparam name="TParams">The parameter type (must be serializable to JSON).</typeparam>
public interface IJob<in TParams> where TParams : notnull
{
    /// <summary>Executes the job with the given parameters.</summary>
    Task ExecuteAsync(TParams parameters, JobContext context, CancellationToken ct);
}
