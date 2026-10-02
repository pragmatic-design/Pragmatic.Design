using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;

namespace Pragmatic.Jobs.Consumer;

/// <summary>
///     Minimal [Job] declaration — proves the Pragmatic.SourceGenerator package
///     fires on this assembly via PackageReference (not ProjectReference). The SG
///     discovery scans the compilation for types with [Job], and the generated
///     PragmaticJobTypeRegistry / GreetingJob_Invoker / AddPragmaticJobs(this
///     IServiceCollection) land in this assembly's namespace.
/// </summary>
[Job]
public sealed partial class GreetingJob : IJob
{
    public Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        Console.WriteLine($"  job executed            : {context.JobId} via SG invoker");
        return Task.CompletedTask;
    }
}
