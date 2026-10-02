using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.Jobs;

/// <summary>
///     Extensions for enabling scheduled messages via Pragmatic.Jobs.
/// </summary>
public static class MessagingJobsExtensions
{
    /// <summary>
    ///     Enables scheduled message delivery backed by <see cref="Pragmatic.Jobs.IJobScheduler"/>.
    ///     Registers <see cref="JobsMessageScheduler"/> as <see cref="IMessageScheduler"/>
    ///     and <see cref="PublishMessageJob"/> for job execution.
    /// </summary>
    public static MessagingBuilder EnableScheduledMessages(this MessagingBuilder builder)
    {
        builder.Services.TryAddScoped<IMessageScheduler, JobsMessageScheduler>();
        builder.Services.TryAddScoped<PublishMessageJob>();

        // ⚠️ And the registry that knows it. Registering the job class alone put it in the container
        // and left the runner unable to name it: the scheduler wrote a row, the processor answered
        // "Unknown job type", and the message that row was carrying had already been acknowledged —
        // so it was not dead-lettered, it was gone.
        PragmaticJobRegistration.AddDiscoveredJobs(builder.Services);

        return builder;
    }
}
