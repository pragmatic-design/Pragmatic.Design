using Pragmatic.Jobs;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     The two durable stores a job test runs against, opened by <see cref="JobStoreUnderTest" /> on the suite's engine, with what they read
///     through (a DbContext, a session's scope), which disposing this releases.
/// </summary>
public sealed class JobStores(IJobStore jobs, IRecurringJobStore recurring, IAsyncDisposable owner) : IAsyncDisposable
{
    public IJobStore Jobs { get; } = jobs;

    public IRecurringJobStore Recurring { get; } = recurring;

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
