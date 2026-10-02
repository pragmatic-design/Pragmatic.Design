using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Providers;

public class PragmaticNullProviderTests
{
    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();

        // Act
        using var provider = new PragmaticNullProvider("TestNull", config);

        // Assert
        provider.Name.Should().Be("TestNull");
        provider.Configuration.Should().Be(config);
        provider.MessagesDiscarded.Should().Be(0);
    }

    [Fact]
    public void ForBenchmarking_ShouldCreateOptimalConfiguration()
    {
        // Act
        var config = PragmaticNullConfiguration.ForBenchmarking();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Trace); // Accept all messages
        config.IncludeStructuredProperties.Should().BeFalse(); // Minimize overhead
        config.IncludeContextEnrichment.Should().BeFalse(); // No context processing

        // Null should not use batching for minimal overhead
        config.Performance.EnableBatching.Should().BeFalse();
        config.Performance.UseZeroAllocation.Should().BeTrue();
        config.Performance.MaxQueueSize.Should().Be(1);

        // Null-optimized formatting (minimal processing)
        config.Formatting.TimestampFormat.Should().Be("s");
        config.Formatting.UseUtcTimestamp.Should().BeFalse();
        config.Formatting.MessageTemplate.Should().Be("{Message}");
        config.Formatting.IncludeExceptionDetails.Should().BeFalse();
        config.Formatting.MaxMessageLength.Should().Be(0);

        // Context filter should have minimal processing
        config.ContextFilter.Mode.Should().Be(ContextFilterMode.Include);
        config.ContextFilter.PropertyNames.Should().BeEmpty();

        // Null-specific custom properties
        config.CustomProperties["BenchmarkMode"].Should().Be(true);
        config.CustomProperties["DiscardMessages"].Should().Be(true);
        config.CustomProperties["MinimalProcessing"].Should().Be(true);
    }

    [Fact]
    public void ForStructuredBenchmarking_ShouldEnableStructuredProperties()
    {
        // Act
        var config = PragmaticNullConfiguration.ForStructuredBenchmarking();

        // Assert
        config.IncludeStructuredProperties.Should().BeTrue(); // Enabled for testing
        config.CustomProperties["TestStructuredProperties"].Should().Be(true);
    }

    [Fact]
    public void ForContextBenchmarking_ShouldEnableContextEnrichment()
    {
        // Act
        var config = PragmaticNullConfiguration.ForContextBenchmarking();

        // Assert
        config.IncludeContextEnrichment.Should().BeTrue(); // Enabled for testing
        config.ContextFilter.Mode.Should().Be(ContextFilterMode.All);
        config.CustomProperties["TestContextEnrichment"].Should().Be(true);
    }

    [Fact]
    public void ForBatchingBenchmarking_ShouldEnableBatching()
    {
        // Act
        var config = PragmaticNullConfiguration.ForBatchingBenchmarking();

        // Assert
        config.Performance.EnableBatching.Should().BeTrue(); // Enabled for testing
        config.Performance.BatchSize.Should().Be(100);
        config.Performance.FlushInterval.Should().Be(TimeSpan.FromMilliseconds(10));
        config.Performance.MaxQueueSize.Should().Be(1000);
        config.CustomProperties["TestBatching"].Should().Be(true);
    }

    [Fact]
    public void ForProductionBenchmarking_ShouldMimicProductionSettings()
    {
        // Act
        var config = PragmaticNullConfiguration.ForProductionBenchmarking();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Information); // Typical production
        config.IncludeStructuredProperties.Should().BeTrue();
        config.IncludeContextEnrichment.Should().BeTrue();

        // Production-like performance settings
        config.Performance.EnableBatching.Should().BeTrue();
        config.Performance.BatchSize.Should().Be(50);
        config.Performance.FlushInterval.Should().Be(TimeSpan.FromSeconds(1));
        config.Performance.MaxQueueSize.Should().Be(5000);

        // Production-like formatting
        config.Formatting.TimestampFormat.Should().Be("yyyy-MM-ddTHH:mm:ss.fffZ");
        config.Formatting.UseUtcTimestamp.Should().BeTrue();
        config.Formatting.IncludeExceptionDetails.Should().BeTrue();
        config.Formatting.MaxMessageLength.Should().Be(32768);

        // Production context filter
        config.ContextFilter.Mode.Should().Be(ContextFilterMode.Include);
        config.ContextFilter.PropertyNames.Should().Contain("CorrelationId");
        config.ContextFilter.PropertyNames.Should().Contain("UserId");
        config.ContextFilter.PropertyNames.Should().Contain("RequestId");

        config.CustomProperties["BenchmarkMode"].Should().Be(true);
        config.CustomProperties["ProductionLike"].Should().Be(true);
        config.CustomProperties["FullProcessing"].Should().Be(true);
    }

    [Fact]
    public void CreateLogger_ShouldReturnLogger()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);

        // Act
        var logger = provider.CreateLogger("Test.Category");

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void IsEnabled_WithConfiguredLevel_ShouldReturnCorrectResult()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        config.MinimumLevel = LogLevel.Warning;
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act & Assert
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
        logger.IsEnabled(LogLevel.Critical).Should().BeTrue();
    }

    [Fact]
    public void LogMessage_ShouldDiscardAndCountMessages()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogInformation("Test message 1");
        logger.LogWarning("Test message 2");
        logger.LogError("Test message 3");

        // Assert
        provider.MessagesDiscarded.Should().Be(3);
    }

    [Fact]
    public void LogMessage_WithStructuredProperties_ShouldStillDiscard()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForStructuredBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogInformation("User {UserId} performed {Action}", 12345, "Login");
        logger.LogWarning("Operation {OperationId} took {Duration}ms", "OP-001", 250);

        // Assert
        provider.MessagesDiscarded.Should().Be(2);
    }

    [Fact]
    public void LogMessage_WithException_ShouldStillDiscard()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        var exception = new InvalidOperationException("Test exception");
        logger.LogError(exception, "An error occurred");

        // Assert
        provider.MessagesDiscarded.Should().Be(1);
    }

    [Fact]
    public async Task LogMessage_Concurrent_ShouldCountCorrectly()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        const int messagesPerThread = 1000;
        const int threadCount = 10;
        const int expectedTotal = messagesPerThread * threadCount;

        // Act
        var tasks = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < messagesPerThread; i++)
                {
                    logger.LogInformation("Message {Index}", i);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        provider.MessagesDiscarded.Should().Be(expectedTotal);
    }

    [Fact]
    public void ResetCounters_ShouldResetToZero()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Message 1");
        logger.LogInformation("Message 2");
        logger.LogInformation("Message 3");

        provider.MessagesDiscarded.Should().Be(3);

        // Act
        provider.ResetCounters();

        // Assert
        provider.MessagesDiscarded.Should().Be(0);
    }

    [Fact]
    public void GetMetrics_ShouldReturnNullSpecificMetrics()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Test message");

        // Act
        var metrics = provider.GetMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CustomMetrics.Should().ContainKey("MessagesDiscarded");
        metrics.CustomMetrics.Should().ContainKey("OutputType");
        metrics.CustomMetrics.Should().ContainKey("BenchmarkMode");
        metrics.CustomMetrics.Should().ContainKey("HasSideEffects");
        metrics.CustomMetrics.Should().ContainKey("ProcessingOverhead");
        metrics.CustomMetrics.Should().ContainKey("ThreadSafe");
        metrics.CustomMetrics.Should().ContainKey("PerformanceImpact");

        metrics.CustomMetrics["MessagesDiscarded"].Should().Be(1L);
        metrics.CustomMetrics["OutputType"].Should().Be("Null");
        metrics.CustomMetrics["BenchmarkMode"].Should().Be(true);
        metrics.CustomMetrics["HasSideEffects"].Should().Be(false);
        metrics.CustomMetrics["ProcessingOverhead"].Should().Be("Minimal");
        metrics.CustomMetrics["ThreadSafe"].Should().Be(true);
        metrics.CustomMetrics["PerformanceImpact"].Should().Be("None");
    }

    [Fact]
    public void CheckHealth_ShouldAlwaysReturnHealthy()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);

        // Act
        var health = provider.CheckHealth();

        // Assert
        health.Should().Be(ProviderHealthStatus.Healthy);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void LogMessage_AllLevels_ShouldDiscard(LogLevel logLevel)
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.Log(logLevel, "Test message");

        // Assert
        provider.MessagesDiscarded.Should().Be(1);
    }

    [Fact]
    public void UpdateConfiguration_ShouldNotAffectDiscarding()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Before update");
        provider.MessagesDiscarded.Should().Be(1);

        // Act
        var newConfig = PragmaticNullConfiguration.ForProductionBenchmarking();
        provider.UpdateConfiguration(newConfig);

        logger.LogInformation("After update");

        // Assert
        provider.MessagesDiscarded.Should().Be(2);
    }

    [Fact]
    public void NullProvider_PerformanceTest_ShouldBeVeryFast()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        const int messageCount = 100000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < messageCount; i++)
        {
            logger.LogInformation("Benchmark message {Index}", i);
        }

        stopwatch.Stop();

        // Assert
        provider.MessagesDiscarded.Should().Be(messageCount);

        // Performance assertion - should be very fast (less than 100ms for 100k messages)
        // This is a rough benchmark, actual performance will vary by machine
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            "Null provider should have minimal overhead for benchmarking");
    }

    [Fact]
    public void NullProvider_WithBatching_ShouldStillDiscardCorrectly()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBatchingBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        for (int i = 0; i < 250; i++) // More than batch size
        {
            logger.LogInformation("Batch message {Index}", i);
        }

        // Give batching time to process
        Thread.Sleep(50);

        // Assert
        provider.MessagesDiscarded.Should().Be(250);
    }

    [Fact]
    public async Task MessagesDiscarded_ShouldBeThreadSafe()
    {
        // Arrange
        var config = PragmaticNullConfiguration.ForBenchmarking();
        using var provider = new PragmaticNullProvider("TestNull", config);
        var logger = provider.CreateLogger("Test.Category");

        const int iterations = 10000;
        const int threadCount = 5;

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    logger.LogInformation("Thread safe message {Index}", i);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        provider.MessagesDiscarded.Should().Be(iterations * threadCount);
    }
}