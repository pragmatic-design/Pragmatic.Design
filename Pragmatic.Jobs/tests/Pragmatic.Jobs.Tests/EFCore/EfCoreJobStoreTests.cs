using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.EFCore;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Jobs.Tests.EFCore;

public class EfCoreJobStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JobsTestDbContext _dbContext;
    private readonly TestClock _clock = new();
    private readonly EfCoreJobStore _store;

    public EfCoreJobStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<JobsTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new JobsTestDbContext(options);
        _dbContext.Database.EnsureCreated();
        _store = new EfCoreJobStore(_dbContext, _clock, NullLogger<EfCoreJobStore>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task EnqueueAsync_SetsStatusToPending()
    {
        var job = CreateJob();
        var result = await _store.EnqueueAsync(job);

        result.Status.Should().Be(JobStatus.Pending);
        result.Id.Should().NotBeEmpty();
        result.CreatedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task EnqueueAsync_GeneratesIdWhenEmpty()
    {
        var job = CreateJob();
        job.Id = Guid.Empty;
        var result = await _store.EnqueueAsync(job);

        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _store.GetAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsJob_WhenExists()
    {
        var job = CreateJob();
        await _store.EnqueueAsync(job);

        var result = await _store.GetAsync(job.Id);
        result.Should().NotBeNull();
        result!.JobType.Should().Be("TestApp.TestJob");
    }

    // GetPendingAsync compares DateTimeOffset values, which SQLite cannot translate. It is asserted on
    // PostgreSQL by Showcase.IntegrationTests/Jobs/JobLeaseIntegrationTests
    // (GetPendingAsync_ReturnsDueJobsOnlyAndRespectsBatchSize), not skipped here.

    [Fact]
    public async Task EnqueueAsync_PersistsAllFields()
    {
        var job = CreateJob();
        job.CorrelationId = "corr-123";
        job.TenantId = "tenant-a";
        job.ParametersJson = "{\"key\":\"value\"}";
        job.ParameterType = "TestParams";

        await _store.EnqueueAsync(job);
        _dbContext.ChangeTracker.Clear();

        var result = await _store.GetAsync(job.Id);
        result!.CorrelationId.Should().Be("corr-123");
        result.TenantId.Should().Be("tenant-a");
        result.ParametersJson.Should().Be("{\"key\":\"value\"}");
        result.ParameterType.Should().Be("TestParams");
    }

    // Note: TryAcquireLeaseAsync, RenewLeaseAsync, ReleaseExpiredLeasesAsync,
    // MarkCompletedAsync, MarkFailedAsync use ExecuteUpdateAsync which requires
    // full relational provider support (not SQLite in-memory for complex expressions).
    // They are covered against PostgreSQL — including a parallel acquisition race — by
    // Showcase.IntegrationTests/Jobs/JobLeaseIntegrationTests.cs.

    private JobInstance CreateJob(DateTimeOffset? scheduledFor = null, int maxAttempts = 3) => new()
    {
        Id = Guid.NewGuid(),
        JobType = "TestApp.TestJob",
        ScheduledFor = scheduledFor ?? _clock.UtcNow.AddMinutes(-1),
        MaxAttempts = maxAttempts,
        Status = JobStatus.Pending
    };
}
