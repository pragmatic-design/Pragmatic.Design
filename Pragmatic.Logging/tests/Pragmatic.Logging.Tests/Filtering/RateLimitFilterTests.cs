using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

public class RateLimitFilterTests
{
    [Fact]
    public void RateLimitFilter_WithinLimit_ShouldContinue()
    {
        // Arrange
        var filter = new RateLimitFilter("Test", TimeSpan.FromMinutes(1), maxMessages: 5);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act - Send 3 messages, all should pass
        var results = new[]
        {
            filter.ShouldLog(logEntry, context),
            filter.ShouldLog(logEntry, context),
            filter.ShouldLog(logEntry, context)
        };

        // Assert
        Assert.All(results, result => Assert.Equal(LogFilterResult.Continue, result));
    }

    [Fact]
    public void RateLimitFilter_ExceedingLimit_ShouldReject()
    {
        // Arrange
        var filter = new RateLimitFilter("Test", TimeSpan.FromMinutes(1), maxMessages: 2);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act - Send 3 messages, first 2 should pass, 3rd should be rejected
        var result1 = filter.ShouldLog(logEntry, context);
        var result2 = filter.ShouldLog(logEntry, context);
        var result3 = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Continue, result1);
        Assert.Equal(LogFilterResult.Continue, result2);
        Assert.Equal(LogFilterResult.Reject, result3);
    }

    [Fact]
    public void RateLimitFilter_DifferentCategories_ShouldTrackSeparately()
    {
        // Arrange
        var filter = new RateLimitFilter("Test", TimeSpan.FromMinutes(1), maxMessages: 1);
        var entry1 = CreateLogEntry("Category1", LogLevel.Information, "Message 1");
        var entry2 = CreateLogEntry("Category2", LogLevel.Information, "Message 2");
        var context = new LogFilterContext();

        // Act - Each category should have its own limit
        var result1 = filter.ShouldLog(entry1, context);
        var result2 = filter.ShouldLog(entry2, context);

        // Assert - Both should pass as they're different categories
        Assert.Equal(LogFilterResult.Continue, result1);
        Assert.Equal(LogFilterResult.Continue, result2);
    }

    [Fact]
    public void RateLimitFilter_DifferentLogLevels_ShouldTrackSeparately()
    {
        // Arrange
        var filter = new RateLimitFilter("Test", TimeSpan.FromMinutes(1), maxMessages: 1);
        var infoEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Info message");
        var errorEntry = CreateLogEntry("Test.Category", LogLevel.Error, "Error message");
        var context = new LogFilterContext();

        // Act - Each log level should have its own limit
        var result1 = filter.ShouldLog(infoEntry, context);
        var result2 = filter.ShouldLog(errorEntry, context);

        // Assert - Both should pass as they're different log levels
        Assert.Equal(LogFilterResult.Continue, result1);
        Assert.Equal(LogFilterResult.Continue, result2);
    }

    [Fact]
    public void RateLimitFilter_SameCategoryAndLevel_ShouldShareLimit()
    {
        // Arrange
        var filter = new RateLimitFilter("Test", TimeSpan.FromMinutes(1), maxMessages: 1);
        var entry1 = CreateLogEntry("Test.Category", LogLevel.Information, "Message 1");
        var entry2 = CreateLogEntry("Test.Category", LogLevel.Information, "Message 2");
        var context = new LogFilterContext();

        // Act
        var result1 = filter.ShouldLog(entry1, context);
        var result2 = filter.ShouldLog(entry2, context);

        // Assert - Second message should be rejected as it's same category:level
        Assert.Equal(LogFilterResult.Continue, result1);
        Assert.Equal(LogFilterResult.Reject, result2);
    }

    [Fact]
    public async Task RateLimitFilter_WithShortTimeWindow_ShouldResetQuickly()
    {
        // Arrange
        var filter = new RateLimitFilter("Test", TimeSpan.FromMilliseconds(100), maxMessages: 1);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act - First message should pass
        var result1 = filter.ShouldLog(logEntry, context);
        Assert.Equal(LogFilterResult.Continue, result1);

        // Second message should be rejected immediately
        var result2 = filter.ShouldLog(logEntry, context);
        Assert.Equal(LogFilterResult.Reject, result2);

        // Wait for time window to reset
        await Task.Delay(150);

        // Third message should pass again
        var result3 = filter.ShouldLog(logEntry, context);
        Assert.Equal(LogFilterResult.Continue, result3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void RateLimitFilter_WithDifferentLimits_ShouldRespectLimit(int maxMessages)
    {
        // Arrange
        var filter = new RateLimitFilter("Test", TimeSpan.FromMinutes(1), maxMessages);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act - Send maxMessages + 1 messages
        var results = new List<LogFilterResult>();
        for (int i = 0; i < maxMessages + 1; i++)
        {
            results.Add(filter.ShouldLog(logEntry, context));
        }

        // Assert - First maxMessages should pass, last one should be rejected
        for (int i = 0; i < maxMessages; i++)
        {
            Assert.Equal(LogFilterResult.Continue, results[i]);
        }
        Assert.Equal(LogFilterResult.Reject, results[maxMessages]);
    }

    [Fact]
    public void RateLimitFilter_Properties_ShouldBeSetCorrectly()
    {
        // Arrange & Act
        var filter = new RateLimitFilter("TestFilter", TimeSpan.FromMinutes(5), maxMessages: 10, priority: 75);

        // Assert
        Assert.Equal("TestFilter", filter.Name);
        Assert.Equal(75, filter.Priority);
    }

    [Fact]
    public void RateLimit_DefaultPriority_ShouldBe50()
    {
        // Arrange & Act
        var filter = new RateLimitFilter("Test", TimeSpan.FromMinutes(1), maxMessages: 5);

        // Assert
        Assert.Equal(50, filter.Priority);
    }

    [Fact]
    public async Task RateLimitFilter_ThreadSafety_ShouldHandleConcurrentAccess()
    {
        // Arrange
        var filter = new RateLimitFilter("ConcurrentTest", TimeSpan.FromMinutes(1), maxMessages: 100);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();
        var allowedCount = 0;
        var rejectedCount = 0;

        // Act - Simulate concurrent access
        var tasks = Enumerable.Range(0, 150).Select(_ => Task.Run(() =>
        {
            var result = filter.ShouldLog(logEntry, context);
            if (result == LogFilterResult.Continue)
                Interlocked.Increment(ref allowedCount);
            else
                Interlocked.Increment(ref rejectedCount);
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert - Should allow exactly 100 messages and reject 50
        Assert.Equal(100, allowedCount);
        Assert.Equal(50, rejectedCount);
    }

    private static LogEntry CreateLogEntry(string category, LogLevel logLevel, string message)
    {
        return new LogEntry
        {
            Category = category,
            LogLevel = logLevel,
            Message = message,
            Timestamp = DateTime.UtcNow,
            Properties = new Dictionary<string, object?>()
        };
    }
}