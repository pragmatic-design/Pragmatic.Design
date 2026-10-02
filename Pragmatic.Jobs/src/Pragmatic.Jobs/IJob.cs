namespace Pragmatic.Jobs;

/// <summary>
///     A background job without parameters.
///     Decorate with <c>[RecurringJob]</c> or <c>[Job]</c> for SG discovery.
/// </summary>
public interface IJob
{
    /// <summary>Executes the job.</summary>
    Task ExecuteAsync(JobContext context, CancellationToken ct);
}
