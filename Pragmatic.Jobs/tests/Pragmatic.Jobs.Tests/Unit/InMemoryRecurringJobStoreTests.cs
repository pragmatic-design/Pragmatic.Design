using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Stores;

namespace Pragmatic.Jobs.Tests.Unit;

public class InMemoryRecurringJobStoreTests
{
    private readonly InMemoryRecurringJobStore _store = new();

    private static RecurringJobDefinition CreateDefinition(string id = "test-job", string cron = "0 * * * *") => new()
    {
        Id = id,
        JobType = "TestNamespace.TestJob",
        CronExpression = cron,
        IsEnabled = true,
        NextExecutionAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task UpsertAsync_InsertsNew()
    {
        var def = CreateDefinition();
        await _store.UpsertAsync(def);

        _store.Count.Should().Be(1);
        var result = await _store.GetAsync("test-job");
        result.Should().NotBeNull();
        result!.CronExpression.Should().Be("0 * * * *");
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExisting()
    {
        await _store.UpsertAsync(CreateDefinition());
        await _store.UpsertAsync(CreateDefinition(cron: "0 2 * * *"));

        _store.Count.Should().Be(1);
        var result = await _store.GetAsync("test-job");
        result!.CronExpression.Should().Be("0 2 * * *");
    }

    [Fact]
    public async Task GetDueAsync_ReturnsOnlyEnabledAndDue()
    {
        var due = CreateDefinition("due");
        var future = CreateDefinition("future");
        future.NextExecutionAt = DateTimeOffset.UtcNow.AddHours(1);
        var disabled = CreateDefinition("disabled");
        disabled.IsEnabled = false;

        await _store.UpsertAsync(due);
        await _store.UpsertAsync(future);
        await _store.UpsertAsync(disabled);

        var result = await _store.GetDueAsync(DateTimeOffset.UtcNow.AddMinutes(1));

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("due");
    }

    [Fact]
    public async Task UpdateNextExecutionAsync_SetsTimestamps()
    {
        await _store.UpsertAsync(CreateDefinition());
        var now = DateTimeOffset.UtcNow;
        var next = now.AddHours(1);

        await _store.UpdateNextExecutionAsync("test-job", next, now);

        var result = await _store.GetAsync("test-job");
        result!.LastExecutedAt.Should().Be(now);
        result.NextExecutionAt.Should().Be(next);
    }

    [Fact]
    public async Task DisableAsync_SetsIsEnabledFalse()
    {
        await _store.UpsertAsync(CreateDefinition());

        await _store.DisableAsync("test-job");

        var result = await _store.GetAsync("test-job");
        result!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task EnableAsync_SetsIsEnabledTrue()
    {
        var def = CreateDefinition();
        def.IsEnabled = false;
        await _store.UpsertAsync(def);

        await _store.EnableAsync("test-job");

        var result = await _store.GetAsync("test-job");
        result!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemovesDefinition()
    {
        await _store.UpsertAsync(CreateDefinition());

        await _store.DeleteAsync("test-job");

        _store.Count.Should().Be(0);
    }
}
