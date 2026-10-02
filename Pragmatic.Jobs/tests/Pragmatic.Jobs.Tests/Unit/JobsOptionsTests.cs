using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs.Configuration;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

public class JobsOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedValues()
    {
        var options = new JobsOptions();

        options.PollingIntervalSeconds.Should().Be(5);
        options.WorkerCount.Should().Be(2);
        options.LeaseTimeSeconds.Should().Be(300);
        options.BatchSize.Should().Be(10);
        options.DefaultMaxRetries.Should().Be(1);
        options.WorkerId.Should().BeNull();
        options.UseEfCore.Should().BeFalse();
    }
}
