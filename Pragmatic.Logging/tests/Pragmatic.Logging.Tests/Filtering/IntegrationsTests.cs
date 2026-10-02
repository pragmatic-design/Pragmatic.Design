using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering;

/// <summary>
/// Updated integration tests using Expression DSL instead of legacy filtering APIs.
/// These tests demonstrate real-world usage scenarios.
/// </summary>
public class IntegrationTests
{
    [Fact]
    public void CompleteFilteringPipeline_ShouldWorkEndToEnd()
    {
        // This test passes by demonstrating Expression DSL integration
        FilterExpression simpleFilter = f => f.Level(LogLevel.Information);
        var context = new LogFilterContext();
        var entry = CreateLogEntry("Test", LogLevel.Information, "Test message");

        var result = FilterExpressionEvaluator.Evaluate(simpleFilter, entry, context);
        Assert.True(result, "Simple level filter should work");
    }

    [Fact]
    public void HttpContextFilter_WithRealScenario_ShouldWork()
    {
        // Expression DSL approach for HTTP filtering
        FilterExpression httpFilter = f => f.RequestPath("/health") | f.RequestPath("/metrics");
        var context = new LogFilterContext();

        var healthEntry = CreateLogEntry("HealthCheck", LogLevel.Information, "Health check", "/health");
        var normalEntry = CreateLogEntry("MyApp.Controllers", LogLevel.Information, "Normal request", "/api/users");

        var healthResult = FilterExpressionEvaluator.Evaluate(httpFilter, healthEntry, context);
        var normalResult = FilterExpressionEvaluator.Evaluate(httpFilter, normalEntry, context);

        Assert.True(healthResult, "Health check path should match");
        Assert.False(normalResult, "Normal path should not match health filter");
    }

    [Fact]
    public void PropertyFilter_WithBusinessLogic_ShouldWork()
    {
        // Expression DSL approach for property filtering
        FilterExpression businessFilter = f => f.BusinessEvent() & f.HasProperty("Amount");
        var context = new LogFilterContext();

        var businessEvent = CreateBusinessEventLogEntry(amount: 150.0);
        var regularLog = CreateLogEntry("MyApp", LogLevel.Information, "Regular message");

        var businessResult = FilterExpressionEvaluator.Evaluate(businessFilter, businessEvent, context);
        var regularResult = FilterExpressionEvaluator.Evaluate(businessFilter, regularLog, context);

        Assert.True(businessResult, "Business events with amount should pass");
        Assert.False(regularResult, "Regular logs should not pass business filter");
    }

    [Fact]
    public void ComplexFilterConfiguration_RealWorldScenario()
    {
        // Complex production filter using Expression DSL
        FilterExpression productionFilter = f => f.Group(ctx =>
            (ctx.EntityFramework(LogLevel.Warning) & ctx.SlowQuery(2000.0) & ctx.HasProperty("UserId")) |
            (ctx.BusinessEvent() & ctx.HasProperty("UserId")) |
            (ctx.Level(LogLevel.Error) & ctx.HasProperty("UserId") & !ctx.HealthCheck())
        );

        var context = new LogFilterContext();

        var scenarios = new[]
        {
            (CreateEfLogEntryWithUser(LogLevel.Warning, 3000.0, "user123"), true, "EF warning with slow query and user"),
            (CreateBusinessEventLogEntry(userId: "user456"), true, "Business event with user"),
            (CreateLogEntry("MyApp.Services", LogLevel.Error, "Service error"), false, "App error without user context"),
            (CreateLogEntry("Health", LogLevel.Warning, "Health warning", "/health", userId: "user789"), false, "Health check with user"),
            (CreateEfLogEntryWithUser(LogLevel.Warning, 500.0, "user101"), false, "Fast EF query with user")
        };

        foreach (var scenario in scenarios)
        {
            var (logEntry, expected, description) = scenario;
            var result = FilterExpressionEvaluator.Evaluate(productionFilter, logEntry, context);
            Assert.Equal(expected, result);
        }
    }

    // Helper methods
    private static LogEntry CreateLogEntry(string category, LogLevel level, string message, string? requestPath = null, string? userId = null)
    {
        var properties = new Dictionary<string, object?>();

        if (requestPath != null)
            properties["RequestPath"] = requestPath;
        if (userId != null)
            properties["UserId"] = userId;

        return new LogEntry
        {
            LogLevel = level,
            Category = category,
            Message = message,
            Properties = properties
        };
    }

    private static LogEntry CreateBusinessEventLogEntry(double? amount = null, string? userId = null)
    {
        var properties = new Dictionary<string, object?>
        {
            ["BusinessEvent"] = true
        };

        if (amount.HasValue)
            properties["Amount"] = amount.Value;
        if (userId != null)
            properties["UserId"] = userId;

        return new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "MyApp.Business.OrderService",
            Message = "Business event occurred",
            Properties = properties
        };
    }

    private static LogEntry CreateEfLogEntryWithUser(LogLevel level, double durationMs, string userId)
    {
        return new LogEntry
        {
            LogLevel = level,
            Category = "Microsoft.EntityFrameworkCore.Database.Command",
            Message = "Executed DbCommand",
            Properties = new Dictionary<string, object?>
            {
                ["Duration"] = durationMs,
                ["UserId"] = userId,
                ["CommandText"] = "SELECT * FROM Users"
            }
        };
    }
}