using Pragmatic.Jobs;
using Pragmatic.Jobs.Attributes;
using Pragmatic.Resilience.Attributes;

namespace Pragmatic.Jobs.Samples;

/// <summary>
///     First link in a two-stage chain: generates a report id, then schedules
///     <see cref="MailReportJob"/> to send it out. Uses the injected
///     <see cref="IJobScheduler"/> to enqueue the follow-up manually — the
///     declarative <c>[Continuation&lt;T&gt;]</c> attribute is read by the SG
///     today but the JobScheduler does not yet copy it onto the JobInstance,
///     so user code wires the chain explicitly. The sample makes this
///     difference visible.
/// </summary>
[Job]
[Continuation<MailReportJob>]
public sealed partial class GenerateReportJob(IJobScheduler scheduler) : IJob
{
    public static Guid LastGeneratedReportId;

    public async Task ExecuteAsync(JobContext context, CancellationToken ct)
    {
        LastGeneratedReportId = Guid.NewGuid();
        Console.WriteLine($"  GenerateReportJob       : produced report {LastGeneratedReportId:N}");
        await scheduler.ScheduleAsync<MailReportJob, MailReportParams>(
            new MailReportParams(LastGeneratedReportId, "ops@example.com"),
            correlationId: context.CorrelationId,
            ct: ct);
    }
}

public record MailReportParams(Guid ReportId, string Recipient);

/// <summary>
///     Second link: receives the report id and fake-emails it. No chain
///     continuation attached — terminal step of the demo.
/// </summary>
[Job]
[Retry(MaxAttempts = 2)]
public sealed partial class MailReportJob : IJob<MailReportParams>
{
    public static MailReportParams? LastMailed;

    public Task ExecuteAsync(MailReportParams parameters, JobContext context, CancellationToken ct)
    {
        LastMailed = parameters;
        Console.WriteLine($"  MailReportJob           : delivered report {parameters.ReportId:N} to {parameters.Recipient}");
        return Task.CompletedTask;
    }
}
