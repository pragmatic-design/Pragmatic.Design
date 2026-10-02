using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs;

namespace Pragmatic.Jobs.Tests.Unit;

public class JobContextTests
{
    [Fact]
    public void JobContext_IsImmutable()
    {
        var context = new JobContext(
            Guid.NewGuid(), "TestJob",
            DateTimeOffset.UtcNow, 0, 3,
            "corr-123", "tenant-1");

        context.JobId.Should().NotBeEmpty();
        context.JobType.Should().Be("TestJob");
        context.Attempt.Should().Be(0);
        context.MaxAttempts.Should().Be(3);
        context.CorrelationId.Should().Be("corr-123");
        context.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void JobContext_DefaultsToNullOptionals()
    {
        var context = new JobContext(Guid.NewGuid(), "TestJob", DateTimeOffset.UtcNow, 0, 1);

        context.CorrelationId.Should().BeNull();
        context.TenantId.Should().BeNull();
    }
}
