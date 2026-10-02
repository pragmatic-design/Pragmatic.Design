using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Schedules a typed-parameter job (IJob&lt;TParams&gt;). The SG-generated
///     type registry handles JSON serialization of the parameters on enqueue
///     and deserialization on execution — no reflection in the hot path.
/// </summary>
public static class ParameterizedJobSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- ScheduleAsync<TJob, TParams> ---");

        using var host = JobsHostBuilder.Build();
        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = host.Services.GetRequiredService<IJobStore>();

        var reminder = new ReminderParams(
            ReservationId: Guid.NewGuid(),
            GuestEmail: "alice@example.com");

        var jobId = await scheduler.ScheduleAsync<SendReminderJob, ReminderParams>(
            reminder, correlationId: "demo-corr-2");
        Console.WriteLine($"  enqueued job            : {jobId}");
        Console.WriteLine($"  parameters              : reservation={reminder.ReservationId:N}");
        Console.WriteLine($"                            email={reminder.GuestEmail}");

        var result = await ScheduleAndAwaitSample.WaitForTerminal(store, jobId, TimeSpan.FromSeconds(15));
        Console.WriteLine($"  terminal status         : {result?.Status.ToString() ?? "timeout"}");

        await host.StopAsync();
        Console.WriteLine();
    }
}
