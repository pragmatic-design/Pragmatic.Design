using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Jobs.EFCore;

namespace Pragmatic.Jobs.Tests.EFCore;

public class EfCoreRecurringJobStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JobsTestDbContext _dbContext;
    private readonly EfCoreRecurringJobStore _store;

    public EfCoreRecurringJobStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<JobsTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new JobsTestDbContext(options);
        _dbContext.Database.EnsureCreated();
        _store = new EfCoreRecurringJobStore(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task UpsertAsync_InsertsNewDefinition()
    {
        var definition = CreateDefinition("test-job");
        await _store.UpsertAsync(definition);

        var stored = await _store.GetAsync("test-job");
        stored.Should().NotBeNull();
        stored!.JobType.Should().Be("TestApp.TestJob");
        stored.CronExpression.Should().Be("0 * * * *");
        stored.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingDefinition()
    {
        await _store.UpsertAsync(CreateDefinition("test-job", "0 * * * *"));
        await _store.UpsertAsync(CreateDefinition("test-job", "0 2 * * *"));

        var stored = await _store.GetAsync("test-job");
        stored!.CronExpression.Should().Be("0 2 * * *");
    }

    // GetDueAsync compares DateTimeOffset values, which SQLite cannot translate. It is asserted on
    // PostgreSQL by Showcase.IntegrationTests/Jobs/JobLeaseIntegrationTests
    // (RecurringStore_GetDueAsync_ReturnsEnabledDueDefinitionsOnly), not skipped here.

    [Fact]
    public async Task DisableAsync_DisablesJob()
    {
        await _store.UpsertAsync(CreateDefinition("test-job"));
        await _store.DisableAsync("test-job");

        var stored = await _store.GetAsync("test-job");
        stored!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task EnableAsync_ReenablesJob()
    {
        await _store.UpsertAsync(CreateDefinition("test-job"));
        await _store.DisableAsync("test-job");
        await _store.EnableAsync("test-job");

        var stored = await _store.GetAsync("test-job");
        stored!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateNextExecutionAsync_UpdatesTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.UpsertAsync(CreateDefinition("test-job"));

        var nextExecution = now.AddHours(1);
        await _store.UpdateNextExecutionAsync("test-job", nextExecution, now);

        var stored = await _store.GetAsync("test-job");
        stored!.NextExecutionAt.Should().Be(nextExecution);
        stored.LastExecutedAt.Should().Be(now);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDefinition()
    {
        await _store.UpsertAsync(CreateDefinition("to-delete"));
        await _store.DeleteAsync("to-delete");

        var stored = await _store.GetAsync("to-delete");
        stored.Should().BeNull();
    }

    private static RecurringJobDefinition CreateDefinition(
        string id,
        string cron = "0 * * * *",
        DateTimeOffset? nextExecution = null) => new()
    {
        Id = id,
        JobType = "TestApp.TestJob",
        CronExpression = cron,
        IsEnabled = true,
        NextExecutionAt = nextExecution ?? DateTimeOffset.UtcNow.AddMinutes(-1)
    };
}
