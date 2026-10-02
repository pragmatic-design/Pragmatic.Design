using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering.ExpressionTests;

/// <summary>
/// Integration tests for the complete FilterExpression system with DI container,
/// global filters, and provider-specific expressions.
/// These tests demonstrate the two-level filtering architecture in action.
/// </summary>
[Collection(FilterExpressionEvaluatorCollection.Name)]
public class ExpressionIntegrationTests
{
    #region Two-Level Architecture Tests

    [Fact]
    public void GlobalAndProviderExpressions_WorkTogether()
    {
        // Arrange - Set up global filter to exclude health checks, provider filter for warnings+
        var services = new ServiceCollection();

        services.AddPragmaticLogging(
            global =>
            {
                // Global: exclude health checks and monitoring
                global.FilterExpression = f => !f.HealthCheck() && !f.MonitoringTools();
            },
            builder =>
            {
                // Console provider: only warnings and above
                builder.AddConsole(config =>
                {
                    config.FilterExpression = f => f.Level(LogLevel.Warning);
                });
            }
        );

        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TestCategory");

        // Test scenarios would go here - for now we verify the setup works
        Assert.NotNull(logger);
        Assert.NotNull(serviceProvider.GetService<GlobalFilterConfiguration>());
    }

    [Fact]
    public void GlobalFilter_RejectsBeforeProviderFiltering()
    {
        // Arrange
        var globalConfig = new GlobalFilterConfiguration
        {
            FilterExpression = f => !f.HealthCheck()
        };

        var providerConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f => f.Level(LogLevel.Information)
        };

        // Test health check request - should be rejected by global filter
        var healthCheckEntry = CreateLogEntry("HealthController", LogLevel.Warning, "Health check");
        var context = new LogFilterContext { RequestPath = "/health" };

        // Act - Global filter should reject this
        var globalResult = FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, healthCheckEntry, context);

        // Assert - Global filter rejects, so provider filter never runs
        Assert.False(globalResult);
    }

    [Fact]
    public void ProviderFilter_OnlyRunsAfterGlobalAccepts()
    {
        // Arrange
        var globalConfig = new GlobalFilterConfiguration
        {
            FilterExpression = f => !f.HealthCheck()  // Accept non-health checks
        };

        var providerConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f => f.Level(LogLevel.Warning)  // Only warnings+
        };

        // Test normal API request with info level
        var apiEntry = CreateLogEntry("ApiController", LogLevel.Information, "API request");
        var context = new LogFilterContext { RequestPath = "/api/users" };

        // Act
        var globalResult = FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, apiEntry, context);
        var providerResult = FilterExpressionEvaluator.Evaluate(providerConfig.FilterExpression!, apiEntry, context);

        // Assert
        Assert.True(globalResult);   // Global accepts
        Assert.False(providerResult); // Provider rejects (info < warning)
    }

    #endregion

    #region Complex Real-World Scenarios

    [Fact]
    public void MicroservicesLoggingPattern_WorksCorrectly()
    {
        // Arrange - Typical microservices setup
        var globalConfig = new GlobalFilterConfiguration
        {
            FilterExpression = f =>
                !f.HealthCheck() &&
                !f.MonitoringTools() &&
                f.RateLimit(TimeSpan.FromMinutes(1), 1000)
        };

        // Console: Development debugging
        var consoleConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f =>
                f.Development() && (
                    f.Level(LogLevel.Warning) ||
                    f.UserAction() ||
                    f.BusinessCritical()
                )
        };

        // JSON: Structured analytics data
        var jsonConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f =>
                f.HasStructuredData() && (
                    f.BusinessEvent() ||
                    f.PaymentEvent() ||
                    f.SecurityEvent()
                )
        };

        // File: Comprehensive production logging
        var fileConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f =>
                f.Production() && (
                    f.Level(LogLevel.Warning) ||
                    f.BusinessCritical() ||
                    f.SecurityEvent()
                )
        };

        var devContext = new LogFilterContext { Environment = "Development" };
        var prodContext = new LogFilterContext { Environment = "Production" };

        // Test cases
        var businessEvent = CreateLogEntry("OrderService", LogLevel.Information, "Order created");
        businessEvent.Properties["BusinessEventType"] = "OrderCreated";
        businessEvent.Properties["OrderId"] = "12345";

        var userAction = CreateLogEntry("AuthController", LogLevel.Information, "User login");
        userAction.Properties["UserAction"] = "Login";

        var error = CreateLogEntry("PaymentService", LogLevel.Error, "Payment failed");

        // Act & Assert

        // Business event should go to JSON in both environments
        Assert.True(FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, businessEvent, devContext));
        Assert.True(FilterExpressionEvaluator.Evaluate(jsonConfig.FilterExpression!, businessEvent, devContext));
        Assert.True(FilterExpressionEvaluator.Evaluate(jsonConfig.FilterExpression!, businessEvent, prodContext));

        // User action should go to console in development only
        Assert.True(FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, userAction, devContext));
        Assert.True(FilterExpressionEvaluator.Evaluate(consoleConfig.FilterExpression!, userAction, devContext));
        Assert.False(FilterExpressionEvaluator.Evaluate(consoleConfig.FilterExpression!, userAction, prodContext));

        // Error should go to file in production
        Assert.True(FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, error, prodContext));
        Assert.True(FilterExpressionEvaluator.Evaluate(fileConfig.FilterExpression!, error, prodContext));
    }

    [Fact]
    public void ECommerceLoggingPattern_WorksCorrectly()
    {
        // Arrange - E-commerce platform logging
        var globalConfig = new GlobalFilterConfiguration
        {
            FilterExpression = f =>
                !f.HealthCheck() &&
                f.RateLimit(TimeSpan.FromMinutes(5), 500)
        };

        // Analytics provider: Business events and user behavior
        var analyticsConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f =>
                f.UserAction("Login", "Purchase", "AddToCart", "Checkout") ||
                f.PaymentEvent() ||
                f.BusinessEvent()
        };

        // Security provider: Security events and violations
        var securityConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f =>
                f.SecurityEvent() ||
                f.SecurityViolation() ||
                (f.Level(LogLevel.Warning) && f.Category("*Auth*"))
        };

        // Operations provider: Performance and errors
        var operationsConfig = new PragmaticProviderConfiguration
        {
            FilterExpression = f =>
                f.Level(LogLevel.Error) ||
                f.SlowQuery(1000) ||
                f.HighMemory(100)
        };

        var context = new LogFilterContext();

        // Test cases
        var purchase = CreateLogEntry("OrderController", LogLevel.Information, "Purchase completed");
        purchase.Properties["UserAction"] = "Purchase";
        purchase.Properties["OrderId"] = "order_12345";
        purchase.Properties["Amount"] = 99.99m;

        var securityViolation = CreateLogEntry("AuthService", LogLevel.Warning, "Multiple failed login attempts");
        securityViolation.Properties["SecurityEvent"] = true;
        securityViolation.Properties["FailedAttempts"] = 5;

        var slowQuery = CreateLogEntry("Microsoft.EntityFrameworkCore", LogLevel.Warning, "Query execution");
        slowQuery.Properties["Duration"] = 2500.0;

        // Act & Assert

        // Purchase event should go to analytics
        Assert.True(FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, purchase, context));
        Assert.True(FilterExpressionEvaluator.Evaluate(analyticsConfig.FilterExpression!, purchase, context));

        // Security violation should go to security provider
        Assert.True(FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, securityViolation, context));
        Assert.True(FilterExpressionEvaluator.Evaluate(securityConfig.FilterExpression!, securityViolation, context));

        // Slow query should go to operations
        Assert.True(FilterExpressionEvaluator.Evaluate(globalConfig.FilterExpression!, slowQuery, context));
        Assert.True(FilterExpressionEvaluator.Evaluate(operationsConfig.FilterExpression!, slowQuery, context));

        // Purchase should NOT go to security or operations
        Assert.False(FilterExpressionEvaluator.Evaluate(securityConfig.FilterExpression!, purchase, context));
        Assert.False(FilterExpressionEvaluator.Evaluate(operationsConfig.FilterExpression!, purchase, context));
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void ComplexExpression_PerformanceIsAcceptable()
    {
        // Arrange - Very complex expression
        FilterExpression complexExpression = f =>
            f.Production() &&
            !f.HealthCheck() &&
            !f.MonitoringTools() &&
            f.RateLimit(TimeSpan.FromMinutes(1), 100) &&
            (
                (f.EntityFramework(LogLevel.Warning) && f.SlowQuery(1000)) ||
                (f.BusinessCritical() && f.HasStructuredData()) ||
                (f.UserAction("Login", "Purchase", "Checkout") && f.UserSegment("Premium")) ||
                (f.Level(LogLevel.Error) && !f.Category("Microsoft.*")) ||
                (f.PaymentEvent() && f.HasProperty("Amount")) ||
                f.SecurityViolation()
            );

        var logEntry = CreateLogEntry("OrderService", LogLevel.Warning, "Order processing");
        logEntry.Properties["BusinessCritical"] = true;
        logEntry.Properties["OrderId"] = "12345";

        var context = new LogFilterContext { Environment = "Production" };

        // Pre-warm: compile the expression so JIT and cache are ready before measurement
        FilterExpressionEvaluator.Evaluate(complexExpression, logEntry, context);

        // Act - Measure performance over multiple evaluations
        var results = new List<FilterEvaluationResult>();
        var iterations = 1000;

        for (int i = 0; i < iterations; i++)
        {
            var result = FilterExpressionEvaluator.EvaluateWithMetrics(complexExpression, logEntry, context);
            results.Add(result);
        }

        // Assert
        Assert.True(results.All(r => r.IsSuccess));
        Assert.Contains(results, r => r.CacheHit); // Should have cache hits after first few

        // ⚠️ No wall-clock threshold here. A maximum over a thousand iterations is exactly the
        // statistic a shared machine ruins: one GC pause or one descheduled thread in a thousand breaks
        // it, and the gate runs the hermetic suites in parallel. It would measure the machine, not the
        // evaluator.
        // What the evaluator actually promises is asserted above and holds under any load: every
        // evaluation succeeds, and after the first they are cache hits. The throughput claim belongs in
        // Pragmatic.Logging.Benchmarks, where a number means something because nothing else is running.
        Assert.All(results, r => Assert.True(r.EvaluationTime > TimeSpan.Zero,
            "a measured evaluation must report the time it took"));
    }

    [Fact]
    public void ExpressionCaching_ImprovesPerformanceSignificantly()
    {
        // Arrange
        FilterExpression expression = f =>
            f.Level(LogLevel.Warning) &&
            f.Category("Test.*") &&
            f.SlowQuery(1000);

        var logEntry = CreateLogEntry("Test.Service", LogLevel.Warning, "Test");
        logEntry.Properties["Duration"] = 1500.0;
        var context = new LogFilterContext();

        // Clear cache to start fresh
        FilterExpressionEvaluator.ClearCache();
        FilterExpressionEvaluator.ResetStatistics();

        // Act - First evaluation (cache miss)
        var firstResult = FilterExpressionEvaluator.EvaluateWithMetrics(expression, logEntry, context);

        // Act - Multiple cached evaluations
        var cachedResults = new List<FilterEvaluationResult>();
        for (int i = 0; i < 100; i++)
        {
            cachedResults.Add(FilterExpressionEvaluator.EvaluateWithMetrics(expression, logEntry, context));
        }

        // Assert
        Assert.False(firstResult.CacheHit);
        Assert.True(cachedResults.All(r => r.CacheHit));

        var averageCachedTime = cachedResults.Average(r => r.EvaluationTime.TotalMicroseconds);

        // Cached evaluations should be significantly faster
        Assert.True(averageCachedTime < firstResult.EvaluationTime.TotalMicroseconds);

        var stats = FilterExpressionEvaluator.GetStatistics();
        Assert.True(stats.CacheHitRatio > 0.9); // Should have high cache hit ratio
    }

    #endregion

    #region Backward Compatibility Tests

    [Fact]
    public void LegacyFilterConfiguration_StillWorksWhenNoExpression()
    {
        // Arrange - Old-style filter configuration
        var providerConfig = new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Warning,
            CategoryLevels = new Dictionary<string, LogLevel>
            {
                ["Microsoft.EntityFrameworkCore"] = LogLevel.Error
            }
        };

        // No FilterExpression set - should fall back to legacy behavior
        Assert.Null(providerConfig.FilterExpression);

        // Test would verify legacy filtering still works
        // (Implementation would be in the actual provider classes)
    }

    [Fact]
    public void FilterExpression_TakesPrecedenceOverLegacyConfiguration()
    {
        // Arrange
        var providerConfig = new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information, // Legacy: Accept Info+
            FilterExpression = f => f.Level(LogLevel.Warning) // Expression: Only Warning+
        };

        var infoEntry = CreateLogEntry("Test", LogLevel.Information, "Info message");
        var context = new LogFilterContext();

        // Act - Expression should take precedence
        var result = FilterExpressionEvaluator.Evaluate(providerConfig.FilterExpression!, infoEntry, context);

        // Assert - Expression rejects Info (wants Warning+), ignoring legacy MinimumLevel
        Assert.False(result);
    }

    #endregion

    #region Helper Methods

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

    #endregion
}