using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

public class NamespaceFilterTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database", LogLevel.Information, LogLevel.Warning, false)]
    [InlineData("Microsoft.EntityFrameworkCore.Database", LogLevel.Warning, LogLevel.Warning, true)]
    [InlineData("Microsoft.EntityFrameworkCore.Database", LogLevel.Error, LogLevel.Warning, true)]
    public void NamespaceFilter_WithMinimumLevel_ShouldFilterCorrectly(string category, LogLevel messageLevel, LogLevel minimumLevel, bool shouldAllow)
    {
        // Arrange
        var filter = new NamespaceFilter("EF", minimumLevel: minimumLevel);
        var logEntry = CreateLogEntry(category, messageLevel, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        var expected = shouldAllow ? LogFilterResult.Continue : LogFilterResult.Reject;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database", new[] { "Microsoft.EntityFrameworkCore.*" }, LogFilterResult.Allow)]
    [InlineData("MyApp.Controllers.UserController", new[] { "MyApp.*" }, LogFilterResult.Allow)]
    [InlineData("System.Net.Http.HttpClient", new[] { "Microsoft.*" }, LogFilterResult.Reject)]
    [InlineData("Microsoft.Extensions.Logging", new[] { "Microsoft.EntityFrameworkCore.*" }, LogFilterResult.Reject)]
    public void NamespaceFilter_WithIncludePatterns_ShouldFilterCorrectly(string category, string[] includePatterns, LogFilterResult expectedResult)
    {
        // Arrange
        var filter = new NamespaceFilter("Test", includePatterns: includePatterns);
        var logEntry = CreateLogEntry(category, LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database", new[] { "Microsoft.EntityFrameworkCore.*" }, LogFilterResult.Reject)]
    [InlineData("MyApp.Controllers.UserController", new[] { "System.*" }, LogFilterResult.Continue)]
    [InlineData("System.Net.Http.HttpClient", new[] { "System.*" }, LogFilterResult.Reject)]
    public void NamespaceFilter_WithExcludePatterns_ShouldFilterCorrectly(string category, string[] excludePatterns, LogFilterResult expectedResult)
    {
        // Arrange
        var filter = new NamespaceFilter("Test", excludePatterns: excludePatterns);
        var logEntry = CreateLogEntry(category, LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void NamespaceFilter_WithBothIncludeAndExclude_ShouldApplyExcludeFirst()
    {
        // Arrange
        var includePatterns = new[] { "Microsoft.*" };
        var excludePatterns = new[] { "Microsoft.EntityFrameworkCore.*" };
        var filter = new NamespaceFilter(
            "Test",
            includePatterns: includePatterns,
            excludePatterns: excludePatterns);
        var logEntry = CreateLogEntry("Microsoft.EntityFrameworkCore.Database", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert - Should be rejected by exclude pattern even though it matches include
        Assert.Equal(LogFilterResult.Reject, result);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore", true)]
    [InlineData("Microsoft.EntityFrameworkCore.Database", "Microsoft.EntityFrameworkCore", true)]
    [InlineData("Microsoft.Extensions", "Microsoft.EntityFrameworkCore", false)]
    [InlineData("MyApp.Controllers", "MyApp.*", true)]
    [InlineData("MyApp.Services.UserService", "MyApp.*", true)]
    [InlineData("OtherApp.Controllers", "MyApp.*", false)]
    public void NamespaceFilter_PatternMatching_ShouldWorkCorrectly(string category, string pattern, bool shouldMatch)
    {
        // Arrange
        var filter = new NamespaceFilter("Test", includePatterns: [pattern]);
        var logEntry = CreateLogEntry(category, LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        var expected = shouldMatch ? LogFilterResult.Allow : LogFilterResult.Reject;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NamespaceFilter_WithEntityFrameworkPattern_ShouldFilterEFLogs()
    {
        // Arrange
        var filter = new NamespaceFilter(
            "EntityFramework",
            includePatterns: ["Microsoft.EntityFrameworkCore.*"],
            minimumLevel: LogLevel.Warning);

        var efLogEntry = CreateLogEntry("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information, "Executing SQL");
        var appLogEntry = CreateLogEntry("MyApp.Controllers.UserController", LogLevel.Information, "User action");
        var context = new LogFilterContext();

        // Act & Assert
        Assert.Equal(LogFilterResult.Reject, filter.ShouldLog(efLogEntry, context)); // Too low level
        Assert.Equal(LogFilterResult.Reject, filter.ShouldLog(appLogEntry, context)); // Wrong category

        // Test with warning level EF log
        var efWarningEntry = CreateLogEntry("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning, "Slow query");
        Assert.Equal(LogFilterResult.Allow, filter.ShouldLog(efWarningEntry, context));
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