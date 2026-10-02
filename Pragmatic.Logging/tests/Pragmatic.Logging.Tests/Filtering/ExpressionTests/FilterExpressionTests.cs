using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Filtering.ExpressionTests;

/// <summary>
/// Comprehensive tests for the FilterExpression DSL system.
/// These tests demonstrate the power and flexibility of expression-based filtering.
/// </summary>
[Collection(FilterExpressionEvaluatorCollection.Name)]
public class FilterExpressionTests
{
    #region Basic Expression Tests

    [Fact]
    public void SimpleLogLevel_Expression_WorksCorrectly()
    {
        // Arrange
        FilterExpression expression = f => f.Level(LogLevel.Warning);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Warning, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void SimpleLogLevel_BelowMinimum_ReturnsFalse()
    {
        // Arrange
        FilterExpression expression = f => f.Level(LogLevel.Warning);
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Logical Operator Tests

    [Fact]
    public void AndOperator_BothTrue_ReturnsTrue()
    {
        // Arrange
        FilterExpression expression = f => f.Level(LogLevel.Information) && f.Category("Test.*");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AndOperator_OneFalse_ReturnsFalse()
    {
        // Arrange
        FilterExpression expression = f => f.Level(LogLevel.Warning) && f.Category("Test.*");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void OrOperator_OneTrue_ReturnsTrue()
    {
        // Arrange
        FilterExpression expression = f => f.Level(LogLevel.Warning) || f.Category("Test.*");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void NotOperator_InvertsResult()
    {
        // Arrange
        FilterExpression expression = f => !f.Category("Microsoft.*");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Information, "Test message");
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ComplexLogicalExpression_WorksCorrectly()
    {
        // Arrange - (EF Warning AND SlowQuery) OR BusinessCritical
        FilterExpression expression = f =>
            (f.EntityFramework(LogLevel.Warning) && f.SlowQuery(1000)) ||
            f.BusinessCritical();

        // Test case 1: EF Warning with slow query
        var slowEfQuery = CreateLogEntry("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning, "Executing SQL");
        slowEfQuery.Properties["Duration"] = 1500.0;
        var context = new LogFilterContext();

        // Act & Assert
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, slowEfQuery, context));

        // Test case 2: Business critical event (non-EF)
        var businessEvent = CreateLogEntry("MyApp.Services", LogLevel.Information, "Critical business event");
        businessEvent.Properties["BusinessCritical"] = true;

        Assert.True(FilterExpressionEvaluator.Evaluate(expression, businessEvent, context));

        // Test case 3: EF Info with slow query (should fail - wrong level)
        var efInfo = CreateLogEntry("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information, "Executing SQL");
        efInfo.Properties["Duration"] = 1500.0;

        Assert.False(FilterExpressionEvaluator.Evaluate(expression, efInfo, context));
    }

    #endregion

    #region Performance Filter Tests

    [Fact]
    public void SlowQuery_Filter_WorksWithDifferentNumericTypes()
    {
        // Arrange
        FilterExpression expression = f => f.SlowQuery(1000);
        var context = new LogFilterContext();

        // Test with double
        var doubleEntry = CreateLogEntry("Test", LogLevel.Information, "Query");
        doubleEntry.Properties["Duration"] = 1500.0;
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, doubleEntry, context));

        // Test with int
        var intEntry = CreateLogEntry("Test", LogLevel.Information, "Query");
        intEntry.Properties["Duration"] = 1500;
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, intEntry, context));

        // Test with string
        var stringEntry = CreateLogEntry("Test", LogLevel.Information, "Query");
        stringEntry.Properties["Duration"] = "1500.5";
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, stringEntry, context));

        // Test below threshold
        var belowEntry = CreateLogEntry("Test", LogLevel.Information, "Query");
        belowEntry.Properties["Duration"] = 500.0;
        Assert.False(FilterExpressionEvaluator.Evaluate(expression, belowEntry, context));
    }

    #endregion

    #region HTTP Context Filter Tests

    [Fact]
    public void HealthCheck_Filter_DetectsHealthCheckPaths()
    {
        // Arrange
        FilterExpression expression = f => !f.HealthCheck();
        var logEntry = CreateLogEntry("HealthController", LogLevel.Information, "Health check");
        var context = new LogFilterContext { RequestPath = "/health" };

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert - should be excluded (false because of !)
        Assert.False(result);
    }

    [Fact]
    public void RequestPath_Filter_MatchesWildcards()
    {
        // Arrange
        FilterExpression expression = f => f.RequestPath("/api/*");
        var logEntry = CreateLogEntry("ApiController", LogLevel.Information, "API call");
        var context = new LogFilterContext { RequestPath = "/api/users/123" };

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Business Context Filter Tests

    [Fact]
    public void BusinessCritical_Filter_DetectsBusinessEvents()
    {
        // Arrange
        FilterExpression expression = f => f.BusinessCritical();
        var logEntry = CreateLogEntry("OrderService", LogLevel.Information, "Order processed");
        logEntry.Properties["BusinessCritical"] = true;
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void UserAction_Filter_WithSpecificActions()
    {
        // Arrange
        FilterExpression expression = f => f.UserAction("Login", "Purchase", "Logout");

        // Test Login action
        var loginEntry = CreateLogEntry("AuthController", LogLevel.Information, "User login");
        loginEntry.Properties["Action"] = "Login";
        var context = new LogFilterContext();

        Assert.True(FilterExpressionEvaluator.Evaluate(expression, loginEntry, context));

        // Test unknown action
        var unknownEntry = CreateLogEntry("AuthController", LogLevel.Information, "User action");
        unknownEntry.Properties["Action"] = "Browse";

        Assert.False(FilterExpressionEvaluator.Evaluate(expression, unknownEntry, context));
    }

    #endregion

    #region Environment Filter Tests

    [Fact]
    public void Environment_Filter_ChecksEnvironmentVariable()
    {
        // Arrange
        FilterExpression expression = f => f.Development();
        var logEntry = CreateLogEntry("Test", LogLevel.Information, "Test");
        var context = new LogFilterContext { Environment = "Development" };

        // Act
        var result = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Real-World Complex Scenarios

    [Fact]
    public void RealWorld_DevelopmentConfiguration_WorksCorrectly()
    {
        // Arrange - Development logging: EF warnings + slow queries OR user actions OR errors
        FilterExpression expression = f =>
            f.Development() && (
                (f.EntityFramework(LogLevel.Warning) && f.SlowQuery(500)) ||
                f.UserAction() ||
                f.Level(LogLevel.Error)
            );

        var context = new LogFilterContext { Environment = "Development" };

        // Test 1: EF slow query warning in development
        var efWarning = CreateLogEntry("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning, "SQL execution");
        efWarning.Properties["Duration"] = 750.0;
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, efWarning, context));

        // Test 2: User action in development
        var userAction = CreateLogEntry("UserController", LogLevel.Information, "User login");
        userAction.Properties["UserAction"] = true;
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, userAction, context));

        // Test 3: Error in development
        var error = CreateLogEntry("OrderService", LogLevel.Error, "Order failed");
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, error, context));

        // Test 4: Production environment (should fail)
        var prodContext = new LogFilterContext { Environment = "Production" };
        Assert.False(FilterExpressionEvaluator.Evaluate(expression, efWarning, prodContext));
    }

    [Fact]
    public void RealWorld_ProductionConfiguration_WorksCorrectly()
    {
        // Arrange - Production: Only errors, business critical events, or slow queries
        FilterExpression expression = f =>
            f.Production() &&
            !f.HealthCheck() &&
            !f.MonitoringTools() && (
                f.Level(LogLevel.Error) ||
                f.BusinessCritical() ||
                (f.EntityFramework(LogLevel.Warning) && f.SlowQuery(2000))
            );

        var context = new LogFilterContext { Environment = "Production" };

        // Test 1: Error in production
        var error = CreateLogEntry("OrderService", LogLevel.Error, "Critical error");
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, error, context));

        // Test 2: Business critical event
        var businessEvent = CreateLogEntry("PaymentService", LogLevel.Information, "Payment processed");
        businessEvent.Properties["BusinessCritical"] = true;
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, businessEvent, context));

        // Test 3: Very slow EF query
        var slowQuery = CreateLogEntry("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning, "SQL");
        slowQuery.Properties["Duration"] = 3000.0;
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, slowQuery, context));

        // Test 4: Regular info message (should be excluded)
        var info = CreateLogEntry("UserService", LogLevel.Information, "User logged in");
        Assert.False(FilterExpressionEvaluator.Evaluate(expression, info, context));

        // Test 5: Health check (should be excluded)
        var healthContext = new LogFilterContext { Environment = "Production", RequestPath = "/health" };
        var health = CreateLogEntry("HealthController", LogLevel.Information, "Health check");
        Assert.False(FilterExpressionEvaluator.Evaluate(expression, health, healthContext));
    }

    [Fact]
    public void RealWorld_AnalyticsConfiguration_WorksCorrectly()
    {
        // Arrange - Analytics: Structured data for business events, user actions, payments
        FilterExpression expression = f =>
            f.HasStructuredData() && (
                f.BusinessEvent() ||
                f.UserAction() ||
                f.PaymentEvent()
            );

        var context = new LogFilterContext();

        // Test 1: Business event with structured data
        var businessEvent = CreateLogEntry("OrderService", LogLevel.Information, "Order created");
        businessEvent.Properties["BusinessEventType"] = "OrderCreated";
        businessEvent.Properties["OrderId"] = "12345";
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, businessEvent, context));

        // Test 2: Payment event
        var paymentEvent = CreateLogEntry("PaymentService", LogLevel.Information, "Payment processed");
        paymentEvent.Properties["PaymentId"] = "pay_12345";
        paymentEvent.Properties["Amount"] = 99.99;
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, paymentEvent, context));

        // Test 3: User action
        var userAction = CreateLogEntry("AuthController", LogLevel.Information, "User login");
        userAction.Properties["UserAction"] = "Login";
        userAction.Properties["UserId"] = "user_12345";
        Assert.True(FilterExpressionEvaluator.Evaluate(expression, userAction, context));

        // Test 4: Log without structured data (should be excluded)
        var simpleLog = CreateLogEntry("TestService", LogLevel.Information, "Simple message");
        Assert.False(FilterExpressionEvaluator.Evaluate(expression, simpleLog, context));
    }

    #endregion

    #region Expression Compilation and Caching Tests

    [Fact]
    public void ExpressionCompilation_CachesCorrectly()
    {
        // Arrange
        FilterExpression expression = f => f.Level(LogLevel.Warning);
        var logEntry = CreateLogEntry("Test", LogLevel.Warning, "Test");
        var context = new LogFilterContext();

        // Act - First evaluation (cache miss)
        var result1 = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);
        var stats1 = FilterExpressionEvaluator.GetStatistics();

        // Act - Second evaluation (cache hit)
        var result2 = FilterExpressionEvaluator.Evaluate(expression, logEntry, context);
        var stats2 = FilterExpressionEvaluator.GetStatistics();

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(stats2.CacheHits > stats1.CacheHits);
    }

    [Fact]
    public void ExpressionEvaluation_WithMetrics_ReturnsPerformanceData()
    {
        // Arrange
        FilterExpression expression = f => f.Level(LogLevel.Warning) && f.Category("Test.*");
        var logEntry = CreateLogEntry("Test.Category", LogLevel.Warning, "Test");
        var context = new LogFilterContext();

        // Act
        var result = FilterExpressionEvaluator.EvaluateWithMetrics(expression, logEntry, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Passed);
        Assert.True(result.EvaluationTime > TimeSpan.Zero);
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