using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

public class PropertyFilterTests
{
    [Fact]
    public void PropertyFilter_WithMissingProperty_ShouldContinue()
    {
        // Arrange
        var filter = new PropertyFilter("Test", "MissingProperty", value => true);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Continue, result);
    }

    [Fact]
    public void PropertyFilter_WithMatchingProperty_ShouldContinue()
    {
        // Arrange
        var filter = new PropertyFilter("Test", "TestProperty", value => value?.ToString() == "ExpectedValue");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        logEntry.Properties["TestProperty"] = "ExpectedValue";
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Continue, result);
    }

    [Fact]
    public void PropertyFilter_WithNonMatchingProperty_ShouldReject()
    {
        // Arrange
        var filter = new PropertyFilter("Test", "TestProperty", value => value?.ToString() == "ExpectedValue");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        logEntry.Properties["TestProperty"] = "DifferentValue";
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Reject, result);
    }

    [Theory]
    [InlineData("TestValue", "TestValue", LogFilterResult.Continue)]
    [InlineData("TestValue", "DifferentValue", LogFilterResult.Reject)]
    [InlineData(123, 123, LogFilterResult.Continue)]
    [InlineData(123, 456, LogFilterResult.Reject)]
    public void PropertyFilter_HasValue_ShouldFilterCorrectly(object? propertyValue, object? expectedValue, LogFilterResult expectedResult)
    {
        // Arrange
        var filter = PropertyFilter.HasValue("TestProperty", expectedValue ?? "null");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        logEntry.Properties["TestProperty"] = propertyValue;
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(expectedResult, result);
    }


    [Theory]
    [InlineData("100.5", 50.0, LogFilterResult.Continue)] // 100.5 > 50.0
    [InlineData("25.0", 50.0, LogFilterResult.Reject)]   // 25.0 < 50.0
    [InlineData("50.0", 50.0, LogFilterResult.Reject)]   // 50.0 == 50.0
    [InlineData("invalid", 50.0, LogFilterResult.Reject)] // Can't parse
    [InlineData(null, 50.0, LogFilterResult.Reject)]     // Null value
    public void PropertyFilter_NumericCondition_ShouldFilterCorrectly(object? propertyValue, double threshold, LogFilterResult expectedResult)
    {
        // Arrange
        var filter = PropertyFilter.NumericCondition("Duration", value => value > threshold);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        logEntry.Properties["Duration"] = propertyValue;
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void PropertyFilter_NumericCondition_WithNumericTypes_ShouldWork()
    {
        // Arrange
        var filter = PropertyFilter.NumericCondition("Value", value => value >= 100);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Test different numeric types
        var testCases = new (object value, LogFilterResult expected)[]
        {
            (150, LogFilterResult.Continue),  // int
            (75, LogFilterResult.Reject),     // int
            (100.5, LogFilterResult.Continue), // double
            (99.9, LogFilterResult.Reject),   // double
            (100m, LogFilterResult.Continue), // decimal
            (50f, LogFilterResult.Reject)     // float
        };

        foreach (var testCase in testCases)
        {
            // Act
            logEntry.Properties["Value"] = testCase.value;
            var result = filter.ShouldLog(logEntry, context);

            // Assert
            Assert.Equal(testCase.expected, result);
        }
    }

    [Fact]
    public void PropertyFilter_WithCustomPredicate_ShouldWork()
    {
        // Arrange
        var filter = new PropertyFilter("StringLength", "Message", value =>
        {
            var str = value?.ToString();
            return str is { Length: > 10 };
        });

        var shortMessageEntry = CreateLogEntry("Test", LogLevel.Information, "Short");
        shortMessageEntry.Properties["Message"] = "Short";

        var longMessageEntry = CreateLogEntry("Test", LogLevel.Information, "This is a very long message");
        longMessageEntry.Properties["Message"] = "This is a very long message";

        var context = new LogFilterContext();

        // Act & Assert
        Assert.Equal(LogFilterResult.Reject, filter.ShouldLog(shortMessageEntry, context));
        Assert.Equal(LogFilterResult.Continue, filter.ShouldLog(longMessageEntry, context));
    }

    [Theory]
    [InlineData("Property[Duration]=100", false)] // Custom name format
    [InlineData("Property[Duration]=NumericCondition", true)] // NumericCondition name
    public void PropertyFilter_FactoryMethods_ShouldSetCorrectNames(string expectedNamePattern, bool isNumericCondition)
    {
        // Arrange
        LogFilter filter;
        if (isNumericCondition)
        {
            filter = PropertyFilter.NumericCondition("Duration", value => value > 100);
        }
        else
        {
            filter = PropertyFilter.HasValue("Duration", 100);
        }

        // Act & Assert
        Assert.Contains(expectedNamePattern, filter.Name);
        Assert.Equal(150, filter.Priority); // Default priority for PropertyFilter
    }

    [Fact]
    public void PropertyFilter_WithNullPredicate_ShouldHandleGracefully()
    {
        // Arrange
        var filter = new PropertyFilter("Test", "TestProperty", value => value == null);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        logEntry.Properties["TestProperty"] = null;
        var context = new LogFilterContext();

        // Act
        var result = filter.ShouldLog(logEntry, context);

        // Assert
        Assert.Equal(LogFilterResult.Continue, result);
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