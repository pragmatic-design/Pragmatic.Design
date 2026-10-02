using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Extensions;

namespace Pragmatic.Jobs.Tests.Unit;

public class JobsBuilderTests
{
    private static JobsOptions Configure(Action<JobsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddPragmaticJobs(configure);
        return services.BuildServiceProvider().GetRequiredService<JobsOptions>();
    }

    [Fact]
    public void WithRetention_SetsDays()
        => Configure(b => b.WithRetention(7)).RetentionDays.Should().Be(7);

    [Fact]
    public void WithRetention_Zero_IsAllowed_DisablesPurging()
        => Configure(b => b.WithRetention(0)).RetentionDays.Should().Be(0);

    [Fact]
    public void WithRetention_Negative_Throws()
    {
        var act = () => Configure(b => b.WithRetention(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithMisfireThreshold_SetsValue()
        => Configure(b => b.WithMisfireThreshold(TimeSpan.FromMinutes(5)))
            .MisfireThreshold.Should().Be(TimeSpan.FromMinutes(5));

    [Fact]
    public void WithMisfireThreshold_NonPositive_Throws()
    {
        var act = () => Configure(b => b.WithMisfireThreshold(TimeSpan.Zero));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithWorkerId_EmptyOrWhitespace_Throws()
    {
        var act = () => Configure(b => b.WithWorkerId("   "));
        act.Should().Throw<ArgumentException>("an empty worker id defeats lease fencing");
    }

    [Fact]
    public void Setters_Chain_AndPersistOntoOptions()
    {
        var options = Configure(b => b
            .WithWorkerCount(4)
            .WithPollingInterval(2)
            .WithBatchSize(50));

        options.WorkerCount.Should().Be(4);
        options.PollingIntervalSeconds.Should().Be(2);
        options.BatchSize.Should().Be(50);
    }

    [Fact]
    public void WithWorkerCount_BelowOne_Throws()
    {
        var act = () => Configure(b => b.WithWorkerCount(0));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
