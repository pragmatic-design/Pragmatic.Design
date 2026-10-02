using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

public class LogFilterTests
{
    [Fact]
    public void LogFilterChain_WithNoFilters_ShouldAllowAllMessages()
    {
        // Arrange
        var chain = new LogFilterChain(Array.Empty<LogFilter>());
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = chain.ShouldLog(logEntry, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void LogFilterChain_WithAllowFilter_ShouldAllowMessage()
    {
        // Arrange
        var filter = new TestFilter("Allow", LogFilterResult.Allow);
        var chain = new LogFilterChain(new[] { filter });
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = chain.ShouldLog(logEntry, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void LogFilterChain_WithRejectFilter_ShouldRejectMessage()
    {
        // Arrange
        var filter = new TestFilter("Reject", LogFilterResult.Reject);
        var chain = new LogFilterChain(new[] { filter });
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = chain.ShouldLog(logEntry, context);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void LogFilterChain_WithMultipleFilters_ShouldRespectPriority()
    {
        // Arrange
        var lowPriorityFilter = new TestFilter("Low", LogFilterResult.Reject, priority: 10);
        var highPriorityFilter = new TestFilter("High", LogFilterResult.Allow, priority: 100);
        var chain = new LogFilterChain(new[] { lowPriorityFilter, highPriorityFilter });
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = chain.ShouldLog(logEntry, context);

        // Assert - High priority filter should execute first and allow
        Assert.True(result);
    }

    [Fact]
    public void LogFilterChain_WithContinueFilters_ShouldProcessAll()
    {
        // Arrange
        var filter1 = new TestFilter("Continue1", LogFilterResult.Continue, priority: 100);
        var filter2 = new TestFilter("Continue2", LogFilterResult.Continue, priority: 50);
        var chain = new LogFilterChain(new[] { filter1, filter2 });
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = chain.ShouldLog(logEntry, context);

        // Assert - Should default to allow when all filters continue
        Assert.True(result);
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

    private sealed class TestFilter : LogFilter
    {
        private readonly LogFilterResult _result;

        public TestFilter(string name, LogFilterResult result, int priority = 100)
        {
            Name = name;
            Priority = priority;
            _result = result;
        }

        public override LogFilterResult ShouldLog(LogEntry logEntry, LogFilterContext context)
        {
            return _result;
        }
    }
}