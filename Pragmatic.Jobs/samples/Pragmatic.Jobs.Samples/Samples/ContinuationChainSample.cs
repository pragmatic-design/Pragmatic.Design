using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Demonstrates a two-stage job chain: <see cref="GenerateReportJob"/>
///     enqueues <see cref="MailReportJob"/> from inside its handler once the
///     report id is known. Both jobs flow through the same processor, share
///     the correlation id, and the sample verifies that the second link
///     actually ran with the first link's output as input.
///
///     The declarative <c>[Continuation&lt;GenerateReportJob&gt;]</c> annotation
///     sits on the first job for documentation value — the SG picks it up but
///     does not yet flow it onto the JobInstance, so user code wires the
///     chain via the injected <see cref="IJobScheduler"/>. Once the runtime
///     closes that gap, this sample will keep working unchanged.
/// </summary>
public static class ContinuationChainSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- Continuation chain (Generate → Mail) ---");
        GenerateReportJob.LastGeneratedReportId = Guid.Empty;
        MailReportJob.LastMailed = null;

        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = host.Services.GetRequiredService<IJobStore>();

        var firstJobId = await scheduler.ScheduleAsync<GenerateReportJob>(correlationId: "chain-demo");
        Console.WriteLine($"  enqueued GenerateReportJob : {firstJobId}");

        // Wait for the first link to finish first — its success proves the
        // scheduler-based continuation fired inside ExecuteAsync.
        var first = await ScheduleAndAwaitSample.WaitForTerminal(store, firstJobId, TimeSpan.FromSeconds(10));
        Console.WriteLine($"  first link status        : {first?.Status.ToString() ?? "timeout"}");

        // The mailing job is enqueued lazily by the first link. We can't ask
        // the store to find a job by type without racing the worker, so a
        // static signal on MailReportJob is the cheapest honest observation.
        var mailed = await WaitForAsync(() => MailReportJob.LastMailed is not null, TimeSpan.FromSeconds(10));
        Console.WriteLine($"  second link executed     : {mailed}");
        Console.WriteLine($"  report id consistent     : {(MailReportJob.LastMailed?.ReportId == GenerateReportJob.LastGeneratedReportId)}");

        await host.StopAsync();
        Console.WriteLine();
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(100);
        }
        return predicate();
    }
}
