using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

/// <summary>
/// Integration tests for Expression DSL that replace the legacy filtering integration tests.
/// These tests demonstrate the Expression DSL working in realistic scenarios.
/// </summary>
[Collection(FilterExpressionEvaluatorCollection.Name)]
public class ExpressionDslIntegrationTests
{
    [Fact]
    public void CompleteExpressionDslPipeline_ShouldWorkEndToEnd()
    {
        // Arrange - Create realistic Expression DSL filters
        FilterExpression productionFilter = f => f.Group(ctx =>
            // Production-ready filter: EF warnings+, business events, security events, errors
            (ctx.EntityFramework(LogLevel.Warning) & ctx.SlowQuery(1000.0)) |
            ctx.BusinessEvent() |
            ctx.SecurityEvent() |
            (ctx.Level(LogLevel.Error) & !ctx.HealthCheck())
        );

        var context = new LogFilterContext();

        // Test cases with expected results
        var testCases = new[]
        {
            // Should pass: EF warning with slow query  
            (CreateEfLogEntry(LogLevel.Warning, 1500.0), true, "EF warning with slow query"),
            
            // Should fail: EF warning with fast query
            (CreateEfLogEntry(LogLevel.Warning, 500.0), false, "EF warning with fast query"),
            
            // Should fail: EF info (below warning threshold)
            (CreateEfLogEntry(LogLevel.Information, 2000.0), false, "EF info level"),
            
            // Should pass: Business event
            (CreateBusinessEventEntry(), true, "Business event"),
            
            // Should pass: Security event
            (CreateSecurityEventEntry(), true, "Security event"),
            
            // Should pass: Application error (non-EF)
            (CreateLogEntry("MyApp.Services", LogLevel.Error, "Service failed"), true, "Application error"),
            
            // Should fail: Health check error (excluded)
            (CreateLogEntry("HealthCheck", LogLevel.Error, "Health check failed", "/health"), false, "Health check error"),
            
            // Should fail: Regular info logs
            (CreateLogEntry("MyApp.Controllers", LogLevel.Information, "User logged in"), false, "Regular info log")
        };

        // Act & Assert
        foreach (var testCase in testCases)
        {
            var (logEntry, shouldPass, description) = testCase;
            var result = FilterExpressionEvaluator.Evaluate(productionFilter, logEntry, context);
            Assert.Equal(shouldPass, result);
        }
    }

    [Fact]
    public void ExpressionDslCaching_ShouldImprovePerformance()
    {
        // Arrange
        FilterExpression complexFilter = f => f.Group(ctx =>
            (ctx.BusinessCritical() | ctx.SecurityEvent()) &
            ctx.Level(LogLevel.Warning) &
            !ctx.HealthCheck() &
            ctx.HasStructuredProperties()
        );

        var logEntry = CreateBusinessEventEntry();
        var context = new LogFilterContext();

        // Clear any existing cache to ensure clean test
        FilterExpressionEvaluator.ClearCache();

        // Act - First evaluation (cache miss)
        var firstResult = FilterExpressionEvaluator.EvaluateWithMetrics(complexFilter, logEntry, context);

        // Act - Multiple cached evaluations
        var cachedResults = new List<FilterEvaluationResult>();
        for (int i = 0; i < 10; i++)
        {
            cachedResults.Add(FilterExpressionEvaluator.EvaluateWithMetrics(complexFilter, logEntry, context));
        }

        // Assert
        Assert.False(firstResult.CacheHit, "First evaluation should be a cache miss");
        Assert.True(cachedResults.All(r => r.CacheHit), "All subsequent evaluations should be cache hits");

        var averageCachedTime = cachedResults.Average(r => r.EvaluationTime.TotalMicroseconds);

        // Cached evaluations should be significantly faster (at least 2x faster)
        Assert.True(averageCachedTime < firstResult.EvaluationTime.TotalMicroseconds / 2,
            $"Cached evaluations ({averageCachedTime:F2}μs) should be much faster than first evaluation ({firstResult.EvaluationTime.TotalMicroseconds:F2}μs)");
    }

    [Fact]
    public void MultipleExpressionFilters_ShouldEvaluateIndependently()
    {
        // Arrange - Different filters for different purposes
        FilterExpression securityFilter = f => f.SecurityEvent() | f.SecurityViolation();
        FilterExpression performanceFilter = f => f.SlowQuery(2000) | f.HighMemory(500);
        FilterExpression businessFilter = f => f.BusinessCritical() | f.PaymentEvent();

        var context = new LogFilterContext();

        var testEntry = CreateLogEntry("MyApp.Security", LogLevel.Warning, "Authentication failed", properties: new Dictionary<string, object?>
        {
            ["SecurityEvent"] = true,
            ["Duration"] = 3000.0, // Slow query
            ["BusinessCritical"] = true
        });

        // Act
        var securityResult = FilterExpressionEvaluator.Evaluate(securityFilter, testEntry, context);
        var performanceResult = FilterExpressionEvaluator.Evaluate(performanceFilter, testEntry, context);
        var businessResult = FilterExpressionEvaluator.Evaluate(businessFilter, testEntry, context);

        // Assert - This entry should match all three filters
        Assert.True(securityResult, "Should match security filter");
        Assert.True(performanceResult, "Should match performance filter");
        Assert.True(businessResult, "Should match business filter");
    }

    [Fact]
    public void ExpressionDslWithFilteringLogic_ShouldIntegrateCorrectly()
    {
        // Arrange - Test expression filter logic without actual provider dependency
        FilterExpression importantLogsFilter = f => f.Group(ctx =>
            ctx.Level(LogLevel.Warning) |
            ctx.BusinessEvent() |
            (ctx.UserAction() & ctx.Level(LogLevel.Information))
        );

        var context = new LogFilterContext();

        // Test cases
        var testEntries = new[]
        {
            CreateLogEntry("MyApp", LogLevel.Warning, "Warning message"), // Should pass
            CreateLogEntry("MyApp", LogLevel.Debug, "Debug message"), // Should fail  
            CreateBusinessEventEntry(), // Should pass
            CreateUserActionEntry(LogLevel.Information), // Should pass
            CreateUserActionEntry(LogLevel.Debug), // Should fail
        };

        // Act - Filter entries
        var passedEntries = new List<LogEntry>();

        foreach (var entry in testEntries)
        {
            if (FilterExpressionEvaluator.Evaluate(importantLogsFilter, entry, context))
            {
                passedEntries.Add(entry);
            }
        }

        // Assert
        Assert.Equal(3, passedEntries.Count); // Warning, Business Event, User Action (Info)

        // Verify specific entries passed through
        Assert.Contains(passedEntries, e => e.LogLevel == LogLevel.Warning);
        Assert.Contains(passedEntries, e => e.Properties?.ContainsKey("BusinessEvent") == true);
        Assert.Contains(passedEntries, e => e.Properties?.ContainsKey("UserAction") == true && e.LogLevel == LogLevel.Information);
    }

    [Fact]
    public void PropertyBasedFiltering_ShouldWorkWithComplexScenarios()
    {
        // Arrange - Complex property-based filter
        FilterExpression complexPropertyFilter = f => f.Group(ctx =>
            // High-value transactions
            (ctx.HasProperty("Amount") & ctx.PaymentEvent()) |
            // Slow user operations  
            (ctx.UserAction() & ctx.SlowQuery(1000)) |
            // Structured business events
            (ctx.BusinessEvent() & ctx.HasStructuredProperties())
        );

        var context = new LogFilterContext();

        // Test cases
        var testCases = new[]
        {
            // High-value payment
            (CreateLogEntry("Payment", LogLevel.Information, "Payment processed", properties: new Dictionary<string, object?>
            {
                ["Amount"] = 999.99m,
                ["PaymentEvent"] = true
            }), true, "High-value payment"),

            // Fast user action (should fail)
            (CreateLogEntry("User", LogLevel.Information, "Quick action", properties: new Dictionary<string, object?>
            {
                ["UserAction"] = "click",
                ["Duration"] = 50.0
            }), false, "Fast user action"),

            // Slow user action (should pass)
            (CreateLogEntry("User", LogLevel.Information, "Slow action", properties: new Dictionary<string, object?>
            {
                ["UserAction"] = "search",
                ["Duration"] = 2000.0
            }), true, "Slow user action"),

            // Structured business event (should pass)
            (CreateLogEntry("Business", LogLevel.Information, "Order created", properties: CreateStructuredBusinessEventProperties()), true, "Structured business event"),

            // Simple business event without structure (should fail)
            (CreateLogEntry("Business", LogLevel.Information, "Simple event", properties: new Dictionary<string, object?>
            {
                ["BusinessEvent"] = true
            }), false, "Simple business event")
        };

        // Act & Assert
        foreach (var testCase in testCases)
        {
            var (logEntry, shouldPass, description) = testCase;
            var result = FilterExpressionEvaluator.Evaluate(complexPropertyFilter, logEntry, context);
            Assert.Equal(shouldPass, result);
        }
    }

    // Helper methods to create test log entries
    private static LogEntry CreateEfLogEntry(LogLevel level, double durationMs)
    {
        return new LogEntry
        {
            LogLevel = level,
            Category = "Microsoft.EntityFrameworkCore.Database.Command",
            Message = "Executed DbCommand",
            Properties = new Dictionary<string, object?>
            {
                ["Duration"] = durationMs,
                ["CommandText"] = "SELECT * FROM Users"
            }
        };
    }

    private static LogEntry CreateBusinessEventEntry()
    {
        return new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "MyApp.Business.OrderService",
            Message = "Order completed",
            Properties = new Dictionary<string, object?>
            {
                ["BusinessEvent"] = true,
                ["OrderId"] = "12345",
                ["Amount"] = 199.99m
            }
        };
    }

    private static LogEntry CreateSecurityEventEntry()
    {
        return new LogEntry
        {
            LogLevel = LogLevel.Warning,
            Category = "MyApp.Security.AuthService",
            Message = "Failed authentication attempt",
            Properties = new Dictionary<string, object?>
            {
                ["SecurityEvent"] = true,
                ["UserId"] = "suspicious_user",
                ["IpAddress"] = "192.168.1.100"
            }
        };
    }

    private static LogEntry CreateUserActionEntry(LogLevel level)
    {
        return new LogEntry
        {
            LogLevel = level,
            Category = "MyApp.Controllers.UserController",
            Message = "User performed action",
            Properties = new Dictionary<string, object?>
            {
                ["UserAction"] = "profile_update",
                ["UserId"] = "user123"
            }
        };
    }

    private static LogEntry CreateLogEntry(string category, LogLevel level, string message, string? requestPath = null, Dictionary<string, object?>? properties = null)
    {
        var props = properties ?? new Dictionary<string, object?>();

        if (requestPath != null)
        {
            props["RequestPath"] = requestPath;
        }

        return new LogEntry
        {
            LogLevel = level,
            Category = category,
            Message = message,
            Properties = props
        };
    }

    private static readonly string[] ItemsArray = ["item1", "item2"];

    private static Dictionary<string, object?> CreateStructuredBusinessEventProperties()
    {
        return new Dictionary<string, object?>
        {
            ["BusinessEvent"] = true,
            ["OrderId"] = "12345",
            ["CustomerId"] = "user123",
            ["Items"] = ItemsArray
        };
    }
}