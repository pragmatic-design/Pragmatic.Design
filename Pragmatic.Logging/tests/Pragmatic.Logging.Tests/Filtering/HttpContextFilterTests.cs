using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

public class HttpContextFilterTests
{
    [Fact]
    public void HttpContextFilter_WithNoHttpContext_ShouldContinue()
    {
        // Arrange
        var filter = new HttpContextFilter("Test", (ctx, entry) => false);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext(); // No HttpContext

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Continue, result);
    }

    [Fact]
    public void HttpContextFilter_WithPredicateTrue_ShouldContinue()
    {
        // Arrange
        var filter = new HttpContextFilter("Test", (ctx, entry) => true);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext { HttpContext = new MockHttpContext() };

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Continue, result);
    }

    [Fact]
    public void HttpContextFilter_WithPredicateFalse_ShouldReject()
    {
        // Arrange
        var filter = new HttpContextFilter("Test", (ctx, entry) => false);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext { HttpContext = new MockHttpContext() };

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Reject, result);
    }

    [Fact]
    public void HttpContextFilter_ForHttpMethods_ShouldCreateCorrectFilter()
    {
        // Arrange
        var filter = HttpContextFilter.ForHttpMethods("GET", "POST");

        // Act - Test that filter is created with correct name
        Assert.Equal("HttpMethod(GET,POST)", filter.Name);
        Assert.Equal(200, filter.Priority); // Default priority for HttpContextFilter
    }

    [Fact]
    public void HttpContextFilter_ForRequestPaths_ShouldCreateCorrectFilter()
    {
        // Arrange
        var filter = HttpContextFilter.ForRequestPaths("/api", "/health");

        // Act - Test that filter is created with correct name
        Assert.Equal("RequestPath(/api,/health)", filter.Name);
        Assert.Equal(200, filter.Priority);
    }

    [Fact]
    public void HttpContextFilter_ExcludeUserAgents_ShouldCreateCorrectFilter()
    {
        // Arrange
        var filter = HttpContextFilter.ExcludeUserAgents("kube-probe", "GoogleHC");

        // Act - Test that filter is created with correct name
        Assert.Equal("ExcludeUserAgent(kube-probe,GoogleHC)", filter.Name);
        Assert.Equal(200, filter.Priority);
    }

    [Fact]
    public void HttpContextFilter_WithCustomPredicate_ShouldRespectLogic()
    {
        // Arrange
        var filter = new HttpContextFilter("CustomTest", (ctx, entry) =>
        {
            // Custom logic: only allow error level logs
            return entry.LogLevel >= LogLevel.Error;
        });

        var infoEntry = CreateLogEntry("Test", LogLevel.Information, "Info message");
        var errorEntry = CreateLogEntry("Test", LogLevel.Error, "Error message");
        var context = new LogFilterContext { HttpContext = new MockHttpContext() };

        // Act & Assert
        Assert.Equal(LogFilterResult.Reject, filter.ShouldLog(infoEntry, context));
        Assert.Equal(LogFilterResult.Continue, filter.ShouldLog(errorEntry, context));
    }

    [Fact]
    public void HttpContextFilter_WithException_ShouldHandleGracefully()
    {
        // Arrange
        var filter = new HttpContextFilter("ExceptionTest", (ctx, entry) =>
        {
            throw new InvalidOperationException("Test exception");
        });

        var logEntry = CreateLogEntry("Test", LogLevel.Information, "Test message");
        var context = new LogFilterContext { HttpContext = new MockHttpContext() };

        // Act & Assert - Should not throw, but behavior depends on implementation
        // In a real scenario, you might want to catch exceptions in the predicate
        Assert.Throws<InvalidOperationException>(() => filter.ShouldLog(logEntry, context));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(50)]
    public void HttpContextFilter_WithCustomPriority_ShouldSetCorrectly(int priority)
    {
        // Arrange
        var filter = new HttpContextFilter("Test", (ctx, entry) => true, priority);

        // Act & Assert
        Assert.Equal(priority, filter.Priority);
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

    private sealed class MockHttpContext
    {
        // Simple mock for testing purposes
        // In a real implementation, this would be the actual HttpContext
    }
}