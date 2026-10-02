using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Providers;

public class PragmaticMemoryProviderTests
{
    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();

        // Act
        using var provider = new PragmaticMemoryProvider("TestMemory", config);

        // Assert
        provider.Name.Should().Be("TestMemory");
        provider.Configuration.Should().Be(config);
        provider.Count.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithInvalidMaxEntries_ShouldThrowException()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.CustomProperties["MaxEntries"] = 0;

        // Act & Assert
        var act = () => new PragmaticMemoryProvider("TestMemory", config);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*MaxEntries must be greater than 0*");
    }

    [Fact]
    public void Constructor_WithTooLargeMaxEntries_ShouldThrowException()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.CustomProperties["MaxEntries"] = 2000000; // Over 1 million limit

        // Act & Assert
        var act = () => new PragmaticMemoryProvider("TestMemory", config);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*cannot exceed 1,000,000*");
    }

    [Fact]
    public void ForMemory_ShouldCreateOptimalConfiguration()
    {
        // Act
        var config = PragmaticMemoryConfiguration.ForMemory();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Trace); // Capture everything for debugging
        config.IncludeStructuredProperties.Should().BeTrue();
        config.IncludeContextEnrichment.Should().BeTrue();

        // Memory should not use batching for immediate debugging
        config.Performance.EnableBatching.Should().BeFalse();
        config.Performance.UseZeroAllocation.Should().BeFalse();

        // All context for debugging
        config.ContextFilter.Mode.Should().Be(ContextFilterMode.All);

        // Memory-optimized formatting
        config.Formatting.TimestampFormat.Should().Be("HH:mm:ss.fff");
        config.Formatting.UseUtcTimestamp.Should().BeFalse(); // Local time for debugging
        config.Formatting.MaxMessageLength.Should().Be(0); // No limit

        // Memory-specific custom properties
        config.CustomProperties["MaxEntries"].Should().Be(10000);
        config.CustomProperties["AutoTruncate"].Should().Be(true);
    }

    [Fact]
    public void ForTesting_ShouldCreateTestOptimizedConfiguration()
    {
        // Act
        var config = PragmaticMemoryConfiguration.ForTesting();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Information); // Less verbose for tests
        config.IncludeContextEnrichment.Should().BeFalse(); // Reduce noise in tests
        config.CustomProperties["MaxEntries"].Should().Be(1000); // Smaller capacity
    }

    [Fact]
    public void ForHighCapacityMemory_ShouldCreateHighCapacityConfiguration()
    {
        // Act
        var config = PragmaticMemoryConfiguration.ForHighCapacityMemory();

        // Assert
        config.CustomProperties["MaxEntries"].Should().Be(100000); // Higher capacity
        config.CustomProperties["AutoTruncate"].Should().Be(true);
    }

    [Fact]
    public void CreateLogger_ShouldReturnLogger()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);

        // Act
        var logger = provider.CreateLogger("Test.Category");

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void LogMessage_ShouldStoreInMemory()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogInformation("Test message");

        // Assert
        provider.Count.Should().Be(1);
        var entries = provider.GetLogEntries();
        entries.Should().HaveCount(1);
        entries[0].Message.Should().Be("Test message");
        entries[0].LogLevel.Should().Be(LogLevel.Information);
        entries[0].Category.Should().Be("Test.Category");
    }

    [Fact]
    public void LogMultipleMessages_ShouldStoreAllInOrder()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogDebug("Debug message");
        logger.LogInformation("Info message");
        logger.LogWarning("Warning message");

        // Assert
        provider.Count.Should().Be(3);
        var entries = provider.GetLogEntries();
        entries[0].Message.Should().Be("Debug message");
        entries[1].Message.Should().Be("Info message");
        entries[2].Message.Should().Be("Warning message");
    }

    [Fact]
    public void GetLogEntries_WithLogLevel_ShouldFilterCorrectly()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogDebug("Debug message");
        logger.LogInformation("Info message");
        logger.LogWarning("Warning message");

        // Act
        var infoEntries = provider.GetLogEntries(LogLevel.Information);

        // Assert
        infoEntries.Should().HaveCount(1);
        infoEntries[0].Message.Should().Be("Info message");
    }

    [Fact]
    public void GetLogEntries_WithCategory_ShouldFilterCorrectly()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger1 = provider.CreateLogger("Category1");
        var logger2 = provider.CreateLogger("Category2");

        logger1.LogInformation("Message from category 1");
        logger2.LogInformation("Message from category 2");
        logger1.LogWarning("Another message from category 1");

        // Act
        var category1Entries = provider.GetLogEntries("Category1");

        // Assert
        category1Entries.Should().HaveCount(2);
        category1Entries.All(e => e.Category == "Category1").Should().BeTrue();
    }

    [Fact]
    public void GetLogEntriesContaining_ShouldFindMatchingMessages()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("User logged in successfully");
        logger.LogWarning("User failed to log in");
        logger.LogError("Database connection failed");

        // Act
        var loginEntries = provider.GetLogEntriesContaining("log");

        // Assert
        loginEntries.Should().HaveCount(2);
        loginEntries.All(e => e.Message.Contains("log", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public void GetLatestLogEntries_ShouldReturnMostRecentFirst()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("First message");
        Thread.Sleep(10); // Ensure different timestamps
        logger.LogInformation("Second message");
        Thread.Sleep(10);
        logger.LogInformation("Third message");

        // Act
        var latest = provider.GetLatestLogEntries(2);

        // Assert
        latest.Should().HaveCount(2);
        latest[0].Message.Should().Be("Third message"); // Most recent first
        latest[1].Message.Should().Be("Second message");
    }

    [Fact]
    public void HasLogEntry_WithPredicate_ShouldReturnCorrectResult()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Test message");

        // Act & Assert
        provider.HasLogEntry(e => e.Message.Contains("Test")).Should().BeTrue();
        provider.HasLogEntry(e => e.Message.Contains("NotFound")).Should().BeFalse();
    }

    [Fact]
    public void HasLogEntry_WithLogLevel_ShouldReturnCorrectResult()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogWarning("Warning message");

        // Act & Assert
        provider.HasLogEntry(LogLevel.Warning).Should().BeTrue();
        provider.HasLogEntry(LogLevel.Error).Should().BeFalse();
    }

    [Fact]
    public void HasLogEntryContaining_ShouldReturnCorrectResult()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("User authentication successful");

        // Act & Assert
        provider.HasLogEntryContaining("authentication").Should().BeTrue();
        provider.HasLogEntryContaining("AUTHENTICATION", ignoreCase: true).Should().BeTrue();
        provider.HasLogEntryContaining("AUTHENTICATION", ignoreCase: false).Should().BeFalse();
        provider.HasLogEntryContaining("notfound").Should().BeFalse();
    }

    [Fact]
    public void Clear_ShouldRemoveAllEntries()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Message 1");
        logger.LogInformation("Message 2");
        provider.Count.Should().Be(2);

        // Act
        provider.Clear();

        // Assert
        provider.Count.Should().Be(0);
        provider.GetLogEntries().Should().BeEmpty();
    }

    [Fact]
    public void GetFormattedMessages_ShouldReturnReadableFormat()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Test message");

        // Act
        var formatted = provider.GetFormattedMessages();

        // Assert
        formatted.Should().HaveCount(1);
        formatted[0].Should().Contain("[INFO]");
        formatted[0].Should().Contain("Test.Category");
        formatted[0].Should().Contain("Test message");
    }

    [Fact]
    public void GetFormattedMessages_WithoutTimestamp_ShouldExcludeTimestamp()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Test message");

        // Act
        var formatted = provider.GetFormattedMessages(includeTimestamp: false);

        // Assert
        formatted.Should().HaveCount(1);
        formatted[0].Should().NotContain("[2"); // Should not contain timestamp pattern
        formatted[0].Should().Contain("[INFO]");
        formatted[0].Should().Contain("Test message");
    }

    [Fact]
    public void AutoTruncate_ShouldLimitMemoryUsage()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.CustomProperties["MaxEntries"] = 5;
        config.CustomProperties["AutoTruncate"] = true;

        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act - Add more entries than the limit
        for (int i = 1; i <= 10; i++)
        {
            logger.LogInformation("Message {Index}", i);
        }

        // Assert
        provider.Count.Should().BeLessOrEqualTo(5); // Should be truncated
        var metrics = provider.GetMetrics();
        metrics.CustomMetrics["EntriesTruncated"].Should().NotBe(0L);
    }

    [Fact]
    public void GetMetrics_ShouldReturnMemorySpecificMetrics()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        logger.LogInformation("Test message");

        // Act
        var metrics = provider.GetMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CustomMetrics.Should().ContainKey("CurrentEntries");
        metrics.CustomMetrics.Should().ContainKey("MaxEntries");
        metrics.CustomMetrics.Should().ContainKey("TotalEntriesReceived");
        metrics.CustomMetrics.Should().ContainKey("EntriesTruncated");
        metrics.CustomMetrics.Should().ContainKey("AutoTruncate");
        metrics.CustomMetrics.Should().ContainKey("MemoryUsageEstimateKB");
        metrics.CustomMetrics.Should().ContainKey("OldestEntryAge");
        metrics.CustomMetrics.Should().ContainKey("NewestEntryAge");

        metrics.CustomMetrics["CurrentEntries"].Should().Be(1);
        metrics.CustomMetrics["TotalEntriesReceived"].Should().Be(1L);
    }

    [Fact]
    public void CheckHealth_ShouldReturnCorrectStatus()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.CustomProperties["MaxEntries"] = 10;
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act & Assert - Healthy when empty
        provider.CheckHealth().Should().Be(ProviderHealthStatus.Healthy);

        // Add entries to 80% capacity - should still be healthy
        for (int i = 0; i < 8; i++)
        {
            logger.LogInformation("Message {Index}", i);
        }
        provider.CheckHealth().Should().Be(ProviderHealthStatus.Healthy);

        // Add entries to 95% capacity - should be warning
        for (int i = 8; i < 10; i++)
        {
            logger.LogInformation("Message {Index}", i);
        }
        provider.CheckHealth().Should().Be(ProviderHealthStatus.Warning);
    }

    [Fact]
    public void LogMessage_WithException_ShouldStoreException()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        var exception = new InvalidOperationException("Test exception");

        // Act
        logger.LogError(exception, "An error occurred");

        // Assert
        var entries = provider.GetLogEntries();
        entries.Should().HaveCount(1);
        entries[0].Exception.Should().NotBeNull();
        entries[0].Exception!.Message.Should().Be("Test exception");

        var formatted = provider.GetFormattedMessages();
        formatted[0].Should().Contain("Exception:");
        formatted[0].Should().Contain("InvalidOperationException");
    }

    [Fact]
    public void LogMessage_WithStructuredProperties_ShouldStoreProperties()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.IncludeStructuredProperties = true;
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogInformation("User {UserId} performed {Action}", 12345, "Login");

        // Assert
        var entries = provider.GetLogEntries();
        entries.Should().HaveCount(1);
        entries[0].Properties.Should().ContainKey("UserId");
        entries[0].Properties.Should().ContainKey("Action");
        entries[0].Properties["UserId"].Should().Be(12345);
        entries[0].Properties["Action"].Should().Be("Login");
    }

    [Fact]
    public void UpdateConfiguration_ShouldUpdateMaxEntries()
    {
        // Arrange
        var config = PragmaticMemoryConfiguration.ForMemory();
        config.CustomProperties["MaxEntries"] = 10;
        using var provider = new PragmaticMemoryProvider("TestMemory", config);
        var logger = provider.CreateLogger("Test.Category");

        // Add some entries
        for (int i = 0; i < 5; i++)
        {
            logger.LogInformation("Message {Index}", i);
        }

        // Act - Update to lower limit
        var newConfig = PragmaticMemoryConfiguration.ForMemory();
        newConfig.CustomProperties["MaxEntries"] = 3;
        newConfig.CustomProperties["AutoTruncate"] = true;
        provider.UpdateConfiguration(newConfig);

        // Assert - Should truncate immediately
        provider.Count.Should().BeLessOrEqualTo(3);
    }
}