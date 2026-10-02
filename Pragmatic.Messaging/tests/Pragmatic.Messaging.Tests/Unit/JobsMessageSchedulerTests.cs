using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.Messaging.Jobs;

namespace Pragmatic.Messaging.Tests.Unit;

public class JobsMessageSchedulerTests
{
    public record OrderReminderMessage(Guid OrderId, string Email);

    [Fact]
    public async Task ScheduleAsync_WithDelay_SchedulesJobWithSerializedMessage()
    {
        var mockJobScheduler = new MockJobScheduler();
        var serializer = new JsonMessageSerializer();
        var scheduler = new JobsMessageScheduler(mockJobScheduler, serializer,
            NullLogger<JobsMessageScheduler>.Instance);

        var msg = new OrderReminderMessage(Guid.NewGuid(), "test@example.com");
        var jobId = await scheduler.ScheduleAsync(msg, TimeSpan.FromMinutes(30));

        jobId.Should().NotBeEmpty();
        mockJobScheduler.LastParams.Should().NotBeNull();
        mockJobScheduler.LastParams!.MessageTypeName.Should().Contain("OrderReminderMessage");
        mockJobScheduler.LastParams.SerializedMessage.Should().Contain("test@example.com");
        mockJobScheduler.LastDelay.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task ScheduleAsync_TwoArg_CapturesAmbientTenantAndStableMessageId()
    {
        var mockJobScheduler = new MockJobScheduler();
        var scheduler = new JobsMessageScheduler(mockJobScheduler, new JsonMessageSerializer(),
            NullLogger<JobsMessageScheduler>.Instance, new StubTenantContext("tenant-x"));

        await scheduler.ScheduleAsync(new OrderReminderMessage(Guid.NewGuid(), "a@b.c"), TimeSpan.FromMinutes(5));

        // The background job has no ambient tenant — the scheduler must capture it now.
        mockJobScheduler.LastParams!.TenantId.Should().Be("tenant-x");
        // A stable dedupe id is stamped at schedule time so a job-retry republish deduplicates.
        mockJobScheduler.LastParams.MessageId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ScheduleAsync_WithScheduledAt_SchedulesJobAtSpecificTime()
    {
        var mockJobScheduler = new MockJobScheduler();
        var serializer = new JsonMessageSerializer();
        var scheduler = new JobsMessageScheduler(mockJobScheduler, serializer,
            NullLogger<JobsMessageScheduler>.Instance);

        var scheduledAt = DateTimeOffset.UtcNow.AddHours(2);
        var msg = new OrderReminderMessage(Guid.NewGuid(), "user@test.com");
        var jobId = await scheduler.ScheduleAsync(msg, scheduledAt);

        jobId.Should().NotBeEmpty();
        mockJobScheduler.LastScheduledFor.Should().Be(scheduledAt);
    }

    [Fact]
    public async Task CancelAsync_DelegatesToJobScheduler()
    {
        var mockJobScheduler = new MockJobScheduler();
        var serializer = new JsonMessageSerializer();
        var scheduler = new JobsMessageScheduler(mockJobScheduler, serializer,
            NullLogger<JobsMessageScheduler>.Instance);

        var jobId = Guid.NewGuid();
        await scheduler.CancelAsync(jobId);

        mockJobScheduler.CancelledJobId.Should().Be(jobId);
    }

    [Fact]
    public async Task ScheduleAsync_NullMessage_Throws()
    {
        var mockJobScheduler = new MockJobScheduler();
        var serializer = new JsonMessageSerializer();
        var scheduler = new JobsMessageScheduler(mockJobScheduler, serializer,
            NullLogger<JobsMessageScheduler>.Instance);

        var act = () => scheduler.ScheduleAsync<OrderReminderMessage>(null!, TimeSpan.FromSeconds(1));
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class StubTenantContext(string? tenantId) : Pragmatic.MultiTenancy.ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => null;
        public bool IsResolved => !string.IsNullOrEmpty(tenantId);
    }

    /// <summary>Minimal mock for IJobScheduler that captures parameters.</summary>
    private sealed class MockJobScheduler : IJobScheduler
    {
        public PublishMessageParams? LastParams { get; private set; }
        public TimeSpan? LastDelay { get; private set; }
        public DateTimeOffset? LastScheduledFor { get; private set; }
        public Guid? CancelledJobId { get; private set; }

        public Task<Guid> ScheduleAsync<TJob>(
            TimeSpan? delay = null, string? correlationId = null,
            JobContinuation? continuation = null, CancellationToken ct = default) where TJob : IJob
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> ScheduleAsync<TJob, TParams>(
            TParams parameters, TimeSpan? delay = null, string? correlationId = null,
            JobContinuation? continuation = null, CancellationToken ct = default)
            where TJob : IJob<TParams> where TParams : notnull
        {
            if (parameters is PublishMessageParams p) LastParams = p;
            LastDelay = delay;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<Guid> ScheduleAtAsync<TJob>(
            DateTimeOffset scheduledFor, string? correlationId = null,
            JobContinuation? continuation = null, CancellationToken ct = default) where TJob : IJob
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> ScheduleAtAsync<TJob, TParams>(
            TParams parameters, DateTimeOffset scheduledFor, string? correlationId = null,
            JobContinuation? continuation = null, CancellationToken ct = default)
            where TJob : IJob<TParams> where TParams : notnull
        {
            if (parameters is PublishMessageParams p) LastParams = p;
            LastScheduledFor = scheduledFor;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task CancelAsync(Guid jobId, CancellationToken ct = default)
        {
            CancelledJobId = jobId;
            return Task.CompletedTask;
        }
    }
}
