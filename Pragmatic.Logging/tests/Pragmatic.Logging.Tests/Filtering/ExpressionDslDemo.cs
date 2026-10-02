using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

/// <summary>
/// Demonstrates that the Expression DSL actually works with real examples.
/// </summary>
[Collection(FilterExpressionEvaluatorCollection.Name)]
public class ExpressionDslDemo
{
    [Fact]
    public void ExpressionDsl_BasicLogLevel_WorksCorrectly()
    {
        // Arrange
        var logEntry = new LogEntry
        {
            LogLevel = LogLevel.Warning,
            Category = "Test.Category",
            Message = "Test message",
            Properties = new Dictionary<string, object?>()
        };

        var context = new LogFilterContext();

        // Act & Assert - Simple level filter
        FilterExpression warningOrHigher = f => f.Level(LogLevel.Warning);
        var result = FilterExpressionEvaluator.Evaluate(warningOrHigher, logEntry, context);

        result.Should().BeTrue("Warning level should pass Warning filter");

        // Test with higher level
        FilterExpression errorOnly = f => f.Level(LogLevel.Error);
        result = FilterExpressionEvaluator.Evaluate(errorOnly, logEntry, context);

        result.Should().BeFalse("Warning level should not pass Error filter");
    }

    [Fact]
    public void ExpressionDsl_CategoryPattern_WorksCorrectly()
    {
        // Arrange
        var logEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "Microsoft.AspNetCore.Hosting",
            Message = "Application starting",
            Properties = new Dictionary<string, object?>()
        };

        var context = new LogFilterContext();

        // Act & Assert - Category pattern matching
        FilterExpression microsoftCategories = f => f.Category("Microsoft.*");
        var result = FilterExpressionEvaluator.Evaluate(microsoftCategories, logEntry, context);

        result.Should().BeTrue("Microsoft.AspNetCore.Hosting should match Microsoft.* pattern");

        // Test non-matching pattern
        FilterExpression systemCategories = f => f.Category("System.*");
        result = FilterExpressionEvaluator.Evaluate(systemCategories, logEntry, context);

        result.Should().BeFalse("Microsoft.AspNetCore.Hosting should not match System.* pattern");
    }

    [Fact]
    public void ExpressionDsl_LogicalOperators_WorkCorrectly()
    {
        // Arrange
        var logEntry = new LogEntry
        {
            LogLevel = LogLevel.Warning,
            Category = "Test.Performance",
            Message = "Slow query detected",
            Properties = new Dictionary<string, object?>
            {
                ["Duration"] = 1500.0, // 1.5 seconds
                ["UserId"] = "user123"
            }
        };

        var context = new LogFilterContext();

        // Act & Assert - AND operation
        FilterExpression warningAndSlow = f => f.Level(LogLevel.Warning) & f.SlowQuery(1000);
        var result = FilterExpressionEvaluator.Evaluate(warningAndSlow, logEntry, context);

        result.Should().BeTrue("Warning level AND slow query (1500ms > 1000ms) should be true");

        // Test OR operation
        FilterExpression errorOrSlow = f => f.Level(LogLevel.Error) | f.SlowQuery(1000);
        result = FilterExpressionEvaluator.Evaluate(errorOrSlow, logEntry, context);

        result.Should().BeTrue("Error level OR slow query should be true (slow query matches)");

        // Test NOT operation
        FilterExpression notError = f => !f.Level(LogLevel.Error);
        result = FilterExpressionEvaluator.Evaluate(notError, logEntry, context);

        result.Should().BeTrue("NOT Error level should be true for Warning level");
    }

    [Fact]
    public void ExpressionDsl_ComplexBusinessLogic_WorksCorrectly()
    {
        // Arrange - Simulate a business transaction log
        var logEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "Business.OrderProcessing",
            Message = "Order completed successfully",
            Properties = new Dictionary<string, object?>
            {
                ["OrderId"] = "ORD-12345",
                ["UserId"] = "premium-user-789",
                ["Amount"] = 2500.00m,
                ["Duration"] = 450.0, // milliseconds
                ["Success"] = true
            }
        };

        var context = new LogFilterContext
        {
            UserId = "premium-user-789",
            Environment = "Production"
        };

        // Act & Assert - Complex business filter
        FilterExpression importantBusinessEvents = f =>
            f.Category("Business.*") &
            (f.HasProperty("Amount") & f.HasProperty("Amount", 2500.00m)) |
            f.Environment("Production") & f.UserSegment("premium");

        var result = FilterExpressionEvaluator.Evaluate(importantBusinessEvents, logEntry, context);

        result.Should().BeTrue("Complex business logic should match");
    }

    [Fact]
    public void ExpressionDsl_PerformanceOptimization_WorksCorrectly()
    {
        // Arrange
        var logEntry = new LogEntry
        {
            LogLevel = LogLevel.Debug,
            Category = "Test.Category",
            Message = "Debug message",
            Properties = new Dictionary<string, object?>()
        };

        var context = new LogFilterContext();

        // Act - Create an expression that should short-circuit
        FilterExpression shortCircuitFalse = f => f.Level(LogLevel.Error) & f.Category("*"); // Error level fails first

        // Evaluate multiple times to test caching
        var result1 = FilterExpressionEvaluator.Evaluate(shortCircuitFalse, logEntry, context);
        var result2 = FilterExpressionEvaluator.Evaluate(shortCircuitFalse, logEntry, context);
        var result3 = FilterExpressionEvaluator.Evaluate(shortCircuitFalse, logEntry, context);

        // Assert
        result1.Should().BeFalse();
        result2.Should().BeFalse();
        result3.Should().BeFalse();

        // Check that caching is working
        var stats = FilterExpressionEvaluator.GetStatistics();
        stats.TotalEvaluations.Should().BeGreaterThan(0);
        stats.CacheHits.Should().BeGreaterThan(0, "Should have cache hits after first evaluation");
    }

    [Fact]
    public void ExpressionDsl_ErrorHandling_WorksCorrectly()
    {
        // Arrange
        var logEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "Test.Category",
            Message = "Test message",
            Properties = new Dictionary<string, object?>()
        };

        var context = new LogFilterContext();

        // Act - Test with potentially problematic expression
        FilterExpression nullPropertyAccess = f => f.HasProperty("NonExistentProperty", "SomeValue");

        // This should not throw, but handle gracefully
        var result = FilterExpressionEvaluator.Evaluate(nullPropertyAccess, logEntry, context);

        // Assert - Should handle gracefully (return false for missing property)
        result.Should().BeFalse("Missing property should be handled gracefully");
    }

    [Fact]
    public void ExpressionDsl_WithMetrics_ProvidesPerformanceData()
    {
        // Arrange
        var logEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "Test.Performance",
            Message = "Performance test",
            Properties = new Dictionary<string, object?>()
        };

        var context = new LogFilterContext();

        // Act
        FilterExpression simpleExpression = f => f.Level(LogLevel.Information);
        var result = FilterExpressionEvaluator.EvaluateWithMetrics(simpleExpression, logEntry, context);

        // Assert
        result.Should().NotBeNull();
        result.Passed.Should().BeTrue();
        result.EvaluationTime.Should().BeGreaterThan(TimeSpan.Zero);
        result.IsSuccess.Should().BeTrue();
        result.Exception.Should().BeNull();
    }
}